using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using FluentValidation;
using Shortener.Application.Abstractions;
using Shortener.Application.Contracts;
using Shortener.Application.Services;
using Shortener.Application.Validators;
using Shortener.Infrastructure.FileStorage;
using Shortener.Infrastructure.Identity;
using Shortener.Infrastructure.Observability;
using Shortener.Infrastructure.Otp;
using Shortener.Infrastructure.Persistence;
using Shortener.Infrastructure.Persistence.Interceptors;
using Shortener.Infrastructure.Queueing;
using Shortener.Infrastructure.Retention;
using Shortener.Infrastructure.Services;
using Shortener.Infrastructure.ShortLinks;
using Shortener.Infrastructure.SmsProviders;
using StackExchange.Redis;

namespace Shortener.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<AuditSaveChangesInterceptor>();
        services.AddDbContext<AppDbContext>((sp, options) =>
            options.UseSqlServer(configuration.GetConnectionString("Default"))
                .AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>()));

        var dataProtectionPath = configuration["DataProtection:KeyPath"];
        var dataProtectionBuilder = services.AddDataProtection().SetApplicationName("Shortener");
        if (!string.IsNullOrEmpty(dataProtectionPath))
        {
            dataProtectionBuilder.PersistKeysToFileSystem(new DirectoryInfo(dataProtectionPath));
        }

        services.AddScoped<ICredentialProtector, CredentialProtector>();

        services.AddIdentity<AppUser, IdentityRole>(options =>
            {
                options.Password.RequiredLength = 10;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequireUppercase = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireDigit = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();

        services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            // AbortOnConnectFail=false so a Redis outage that happens after startup is retried
            // in the background and self-heals once Redis comes back, instead of leaving the
            // singleton multiplexer requiring a process restart to recover (§M4.1/§M8 — Redis
            // being down must degrade gracefully, e.g. via ILinkCache's SQL fallback, not wedge).
            // It also means a Redis outage at startup no longer crashes the host; /health/ready
            // (§M8.3) already reports that condition instead.
            var redisOptions = ConfigurationOptions.Parse(configuration.GetConnectionString("Redis")!);
            redisOptions.AbortOnConnectFail = false;
            return ConnectionMultiplexer.Connect(redisOptions);
        });

        services.Configure<FileStorageOptions>(configuration.GetSection("FileStorage"));
        services.AddSingleton<IFileStorage, FileSystemStorage>();

        services.Configure<QueueOptions>(configuration.GetSection("Queue"));

        services.AddScoped<IRetentionResolver, RetentionResolver>();

        services.AddScoped<ISmsProviderFactory, SmsProviderFactory>();
        if (environment.IsDevelopment())
        {
            services.AddKeyedScoped<ISmsProvider, FakeSmsProvider>("fake");
        }

        services.AddKeyedScoped<ISmsProvider, KavenegarProvider>("kavenegar");
        services.AddHttpClient(SmsHttpClientNames.Kavenegar, c => c.BaseAddress = new Uri("https://api.kavenegar.com"))
            .AddSmsRetryPolicy();

        services.AddKeyedScoped<ISmsProvider, MeliPayamakProvider>("melipayamak");
        services.AddHttpClient(SmsHttpClientNames.MeliPayamak, c => c.BaseAddress = new Uri("https://rest.payamak-panel.com"))
            .AddSmsRetryPolicy();

        services.AddKeyedScoped<ISmsProvider, FarazSmsProvider>("farazsms");
        services.AddHttpClient(SmsHttpClientNames.FarazSms, c => c.BaseAddress = new Uri("https://edge.ippanel.com"))
            .AddSmsRetryPolicy();

        services.AddScoped<SmsStreamMessageProcessor>();
        services.AddScoped<StreamClaimService>();
        services.AddScoped<LinkSmsOutboxResolver>();
        services.AddScoped<DeliveryStatusPoller>();

        var shortLinkSection = configuration.GetSection("ShortLink");
        services.Configure<ShortLinkOptions>(shortLinkSection);
        services.AddSingleton(new ShortCodeGeneratorSettings(
            CodeLength: shortLinkSection.GetValue("CodeLength", 6),
            MaxGenerationRetries: shortLinkSection.GetValue("MaxGenerationRetries", 5)));
        services.AddScoped<ICodeReservationStore, RedisCodeReservationStore>();
        services.AddScoped<ShortCodeGenerator>();

        services.AddSingleton<ApiKeyUsageChannel>();
        services.AddScoped<IApiKeyLookupService, ApiKeyLookupService>();
        services.AddScoped<IApiKeyGenerator, ApiKeyGenerator>();

        services.AddScoped<ILinkCache, RedisLinkCache>();
        services.AddScoped<IUploadLinkService, UploadLinkService>();
        services.AddScoped<ILinkSmsStatusResolver, LinkSmsStatusResolver>();
        services.AddScoped<IValidator<UploadLinkMetadata>, UploadLinkMetadataValidator>();
        services.AddScoped<IShortLinkStateResolver, ShortLinkStateResolver>();

        services.Configure<OtpOptions>(configuration.GetSection("Otp"));
        services.Configure<DownloadOptions>(configuration.GetSection("Download"));
        services.AddScoped<IOtpService, OtpService>();
        services.AddScoped<IDownloadTokenService, DownloadTokenService>();

        services.AddSingleton<LinkAccessLogChannel>();
        services.AddSingleton<ILinkAccessLogger>(sp => sp.GetRequiredService<LinkAccessLogChannel>());

        services.AddScoped<IDistributedLock, RedisDistributedLock>();

        services.Configure<RetentionJobOptions>(configuration.GetSection("Retention"));
        services.AddScoped<IRetentionJobRunner, RetentionJobRunner>();
        services.AddScoped<IOrphanScanJobRunner, OrphanScanJobRunner>();
        services.AddSingleton<DiskSpaceHealthCheck>();
        services.AddSingleton<OtpQueueLagHealthCheck>();
        services.AddScoped<OtpAccountCoverageHealthCheck>();

        services.AddShortenerMetrics();

        return services;
    }
}
