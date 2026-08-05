using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shortener.Application.Abstractions;
using Shortener.Domain.Entities;
using Shortener.Domain.Enums;
using Shortener.Infrastructure.Persistence;
using Testcontainers.MsSql;
using Testcontainers.Redis;

namespace Shortener.IntegrationTests;

/// <summary>
/// §M8.4 — boots the real Api host (WebApplicationFactory&lt;Program&gt;) against ephemeral
/// Testcontainers MsSql/Redis instances instead of the shared local dev database. Every test run
/// gets a completely fresh schema and empty Redis, so accumulated test data can no longer leak
/// between runs or confuse live manual verification against the dev environment (both real problems
/// encountered while this repo was still on the "reuse the dev DB" fixture).
///
/// Shared at the collection level (see IntegrationTestCollection) rather than per test class —
/// starting SQL Server alone takes real wall-clock time, and paying that once per test run instead
/// of once per class is the entire point. A side effect: xUnit never runs tests in the same
/// collection concurrently, so every integration test in this project now runs sequentially against
/// one shared container pair — trading some wall-clock time for eliminating cross-test races.
/// </summary>
public sealed class ApiTestFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MsSqlContainer _sqlContainer = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
        .WithPassword("Test_Passw0rd!")
        .Build();
    private readonly RedisContainer _redisContainer = new RedisBuilder("redis:7-alpine")
        .Build();

    private readonly SemaphoreSlim _seedLock = new(1, 1);
    public string ApiKey { get; private set; } = string.Empty;

    async Task IAsyncLifetime.InitializeAsync()
    {
        await Task.WhenAll(_sqlContainer.StartAsync(), _redisContainer.StartAsync());

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(_sqlContainer.GetConnectionString())
            .Options;
        await using (var db = new AppDbContext(options))
        {
            // Tests are the one place Database.MigrateAsync() at startup is fine — the doc's "never
            // call Migrate() in Startup" rule is about avoiding concurrent-instance races in a real
            // deployment, which doesn't apply to a fixture that owns a brand-new, single-consumer container.
            await db.Database.MigrateAsync();
        }

        // Forces WebApplicationFactory to actually build the host now (Services is normally lazy),
        // which runs Program.cs's Development-only DbSeeder.SeedAsync call.
        _ = Services;
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _sqlContainer.DisposeAsync();
        await _redisContainer.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _sqlContainer.GetConnectionString(),
                ["ConnectionStrings:Redis"] = _redisContainer.GetConnectionString(),
            });
        });
        builder.ConfigureServices(services =>
            services.AddKeyedScoped<ISmsProvider, TestControlledSmsProvider>(TestControlledSmsProvider.Code));
    }

    public async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        if (string.IsNullOrEmpty(ApiKey))
        {
            await _seedLock.WaitAsync();
            try
            {
                if (string.IsNullOrEmpty(ApiKey))
                {
                    await SeedTestClientAsync();
                }
            }
            finally
            {
                _seedLock.Release();
            }
        }

        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);
        return client;
    }

    private async Task SeedTestClientAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var client = new Client { Name = "IntegrationTest", Code = $"ITEST-{Guid.NewGuid():N}"[..20], IsActive = true };
        db.Clients.Add(client);
        await db.SaveChangesAsync();

        var rawKeyBytes = RandomNumberGenerator.GetBytes(32);
        var rawKey = "sk_" + Convert.ToBase64String(rawKeyBytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawKey))).ToLowerInvariant();

        db.ApiKeys.Add(new ApiKey
        {
            ClientId = client.Id,
            KeyHash = hash,
            KeyPrefix = rawKey[..12],
            Title = "Integration test key",
            IsActive = true,
        });

        // DbSeeder's "fake" SmsProvider already exists (it runs at host startup for Development).
        // IOtpService requires an Otp-purpose account + a global Otp template to do anything besides
        // report NotConfigured, so the test client needs both, same as the DbSeeder-seeded dev client.
        var fakeProvider = await db.SmsProviders.SingleAsync(p => p.Code == "fake");
        db.SmsAccounts.Add(new SmsAccount
        {
            ClientId = client.Id,
            SmsProviderId = fakeProvider.Id,
            Title = "Test Otp Account",
            Purpose = SmsAccountPurpose.Otp,
            RatePerMinute = 3000,
            IsDefault = true,
            IsActive = true,
        });
        db.MessageTemplates.Add(new MessageTemplate
        {
            ClientId = client.Id,
            ReportId = null,
            TemplateType = TemplateType.Otp,
            Title = "Test Otp Template",
            Body = "کد تایید شما: {otp} (اعتبار {otpMinutes} دقیقه)",
            IsActive = true,
        });

        await db.SaveChangesAsync();

        ApiKey = rawKey;
    }
}

[CollectionDefinition(Name)]
public sealed class IntegrationTestCollection : ICollectionFixture<ApiTestFixture>
{
    public const string Name = "Integration";
}
