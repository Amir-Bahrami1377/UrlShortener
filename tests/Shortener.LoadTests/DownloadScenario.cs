using System.Collections.Concurrent;
using NBomber.CSharp;
using StackExchange.Redis;

namespace Shortener.LoadTests;

/// <summary>§M8.4d row 2 — 100 concurrent users, p95 &lt; 1s. Download tokens are GETDEL one-shot
/// (doc §M4.5), so this pre-provisions one valid token per simulated user during an untimed setup
/// phase, then bursts all 100 GET /d/{token} calls at once — isolating the file-streaming endpoint's
/// own concurrency behavior from OTP-flow latency (that's a separate concern, already covered by M4's
/// test suite and this project's queue-priority scenario).</summary>
public static class DownloadScenario
{
    private const int RealFlowSampleCount = 2;
    private const int DownloadTokenTtlSeconds = 300; // doc §M4.1 DownloadOptions.TokenTtlSeconds default.

    public static async Task RunAsync(bool smokeTest)
    {
        var userCount = smokeTest ? 10 : 100;
        Console.WriteLine($"[Setup] Provisioning {userCount} download tokens ({RealFlowSampleCount} via the real OTP flow as a sanity check, the rest seeded directly)...");

        var apiClient = SetupHelpers.CreateApiClient();
        var publicWebClient = SetupHelpers.CreatePublicWebClient();
        var mux = await ConnectionMultiplexer.ConnectAsync(LoadTestConfig.RedisConnectionString);
        var redisDb = mux.GetDatabase();
        var smallFile = "%PDF-1.4\ntiny load-test file"u8.ToArray();

        var tokens = new ConcurrentQueue<string>();
        for (var i = 0; i < userCount; i++)
        {
            var link = await SetupHelpers.UploadAsync(apiClient, smallFile, sendSmsImmediately: false);

            string token;
            if (i < RealFlowSampleCount)
            {
                await SetupHelpers.ClearOtpIpRateLimitAsync();
                token = await SetupHelpers.GetDownloadTokenViaRealOtpFlowAsync(publicWebClient, link);
            }
            else
            {
                token = await SetupHelpers.SeedDownloadTokenDirectlyAsync(redisDb, link, DownloadTokenTtlSeconds);
            }

            tokens.Enqueue(token);

            if ((i + 1) % 20 == 0)
            {
                Console.WriteLine($"[Setup] {i + 1}/{userCount} tokens ready.");
            }
        }

        Console.WriteLine("[Setup] Done. Starting the burst.");

        var scenario = Scenario.Create("concurrent_download", async _ =>
            {
                if (!tokens.TryDequeue(out var token))
                {
                    return Response.Fail(statusCode: "NO_TOKEN_LEFT");
                }

                using var httpResponse = await publicWebClient.GetAsync($"/d/{token}");
                return httpResponse.IsSuccessStatusCode
                    ? Response.Ok(statusCode: ((int)httpResponse.StatusCode).ToString())
                    : Response.Fail(statusCode: ((int)httpResponse.StatusCode).ToString());
            })
            .WithoutWarmUp()
            // IterationsForConstant: userCount virtual users truly concurrent, stopping automatically
            // once exactly userCount iterations total have run — unlike KeepConstant's duration-based
            // stop, this doesn't leave emptied-queue vusers spinning on NO_TOKEN_LEFT for the rest of
            // the window (that produced hundreds of thousands of harmless-but-alarming fails).
            .WithLoadSimulations([Simulation.IterationsForConstant(copies: userCount, iterations: userCount)]);

        NBomberRunner.RegisterScenarios(scenario)
            .WithReportFolder(Path.Combine(AppContext.BaseDirectory, "reports", "download"))
            .WithReportFormats(NBomber.Contracts.Stats.ReportFormat.Txt, NBomber.Contracts.Stats.ReportFormat.Md)
            .Run();

        await mux.CloseAsync();
    }
}
