using System.Diagnostics;
using Microsoft.Data.SqlClient;
using StackExchange.Redis;

namespace Shortener.LoadTests;

/// <summary>§M8.4d row 3 — 100,000 bulk messages + scattered OTP requests, OTP latency must stay
/// &lt; 10s. This is the whole architecture's single most important guarantee (doc Appendix C): the
/// OTP stream/consumer-group must never be delayed by bulk backlog. Bulk entries reference synthetic,
/// nonexistent SmsMessageIds — SmsStreamMessageProcessor treats an unresolvable id as AlreadyHandled
/// (ack-and-skip), so the Worker still has to read and discard all 100k, exercising real throughput,
/// without needing 100k real DB rows (already proven at 10k+ scale earlier in this build).</summary>
public static class QueuePriorityScenario
{
    public static async Task RunAsync(bool smokeTest)
    {
        var bulkCount = smokeTest ? 2_000 : 100_000;
        var otpSamples = smokeTest ? 2 : 5;

        var mux = await ConnectionMultiplexer.ConnectAsync(LoadTestConfig.RedisConnectionString);
        var redisDb = mux.GetDatabase();

        Console.WriteLine($"[QueuePriority] Seeding {bulkCount:N0} synthetic sms:bulk entries...");
        var seedSw = Stopwatch.StartNew();
        const int batchSize = 1000;
        for (var batchStart = 0; batchStart < bulkCount; batchStart += batchSize)
        {
            var batch = new List<Task>();
            var end = Math.Min(batchStart + batchSize, bulkCount);
            for (var i = batchStart; i < end; i++)
            {
                batch.Add(redisDb.StreamAddAsync("sms:bulk", "smsMessageId", 900_000_000L + i));
            }

            await Task.WhenAll(batch);
        }

        Console.WriteLine($"[QueuePriority] Seeded {bulkCount:N0} entries in {seedSw.Elapsed.TotalSeconds:N1}s.");

        var apiClient = SetupHelpers.CreateApiClient();
        var publicWebClient = SetupHelpers.CreatePublicWebClient();
        var smallFile = "%PDF-1.4\ntiny load-test file"u8.ToArray();
        var latencies = new List<TimeSpan>();

        for (var sample = 0; sample < otpSamples; sample++)
        {
            if (sample > 0)
            {
                await SetupHelpers.ClearOtpIpRateLimitAsync();
                await Task.Delay(TimeSpan.FromSeconds(smokeTest ? 2 : 20)); // "scattered", not all at once.
            }

            var link = await SetupHelpers.UploadAsync(apiClient, smallFile, sendSmsImmediately: false);

            var sw = Stopwatch.StartNew();
            var requestResponse = await publicWebClient.PostAsync($"/s/{link.Code}/otp/request", content: null);
            requestResponse.EnsureSuccessStatusCode();

            await WaitForSentAsync(link.ShortLinkId);
            sw.Stop();

            latencies.Add(sw.Elapsed);
            Console.WriteLine($"[QueuePriority] Sample {sample + 1}/{otpSamples}: OTP latency = {sw.Elapsed.TotalMilliseconds:N0}ms " +
                $"(bulk backlog still unacked: {await GetBulkPendingCountAsync(redisDb)} entries)");
        }

        await mux.CloseAsync();

        var max = latencies.Max();
        var avg = TimeSpan.FromMilliseconds(latencies.Average(t => t.TotalMilliseconds));
        Console.WriteLine();
        Console.WriteLine("=== Queue Priority Result ===");
        Console.WriteLine($"Samples: {latencies.Count}, Avg: {avg.TotalMilliseconds:N0}ms, Max: {max.TotalMilliseconds:N0}ms");
        Console.WriteLine(max.TotalSeconds < 10
            ? "PASS — OTP latency stayed under 10s despite the bulk backlog."
            : "FAIL — OTP latency exceeded the 10s target.");
    }

    /// <summary>Unacked count, not stream length — XLEN never shrinks between XTRIMs regardless of
    /// how many entries the Worker has already consumed and acked, which would make "still draining"
    /// misleadingly look unchanged even after the Worker races through nearly the whole backlog.</summary>
    private static async Task<long> GetBulkPendingCountAsync(IDatabase redisDb) =>
        (await redisDb.StreamPendingAsync("sms:bulk", "bulk-workers")).PendingMessageCount;

    private static async Task WaitForSentAsync(long shortLinkId)
    {
        await using var conn = new SqlConnection(LoadTestConfig.SqlConnectionString);
        await conn.OpenAsync();

        // Headroom past the 10s target on purpose: a FAIL should still print a real measured
        // latency, not throw before the number is even known.
        for (var attempt = 0; attempt < 300; attempt++)
        {
            var cmd = new SqlCommand(
                "SELECT TOP 1 Status FROM SmsMessages WHERE ShortLinkId = @id AND MessageType = 1 ORDER BY Id DESC", conn);
            cmd.Parameters.AddWithValue("@id", shortLinkId);
            var status = await cmd.ExecuteScalarAsync();
            if (status is byte b && b == 3) // SmsStatus.Sent = 3
            {
                return;
            }

            await Task.Delay(100);
        }

        throw new InvalidOperationException($"OTP SmsMessage for ShortLinkId={shortLinkId} never reached Sent within 30s.");
    }
}
