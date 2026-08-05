using Microsoft.Extensions.Options;
using Shortener.Infrastructure.Queueing;
using StackExchange.Redis;

namespace Shortener.Worker.Workers;

/// <summary>
/// §M5.3 — the latency-critical OTP consumer: batch size 1, no rate limiting, target latency
/// &lt; 10 seconds. Per-message send logic (idempotency, retry backoff, dead-lettering on permanent
/// failure) lives in SmsStreamMessageProcessor/StreamConsumerWorkerBase, shared with BulkSmsWorker.
/// </summary>
public sealed class OtpSmsWorker(
    IConnectionMultiplexer redis,
    IServiceScopeFactory scopeFactory,
    IOptions<QueueOptions> queueOptions,
    ILogger<OtpSmsWorker> logger)
    : StreamConsumerWorkerBase(redis, scopeFactory, logger)
{
    private readonly QueueOptions _options = queueOptions.Value;

    protected override string Stream => _options.OtpStream;
    protected override string ConsumerGroup => _options.OtpConsumerGroup;
    protected override string DeadStream => _options.DeadStream;
    protected override int BatchSize => 1;
    protected override string ConsumerNamePrefix => "otp";

    protected override async Task ProcessBatchAsync(IDatabase db, IServiceProvider services, StreamEntry[] entries, CancellationToken ct)
    {
        foreach (var entry in entries)
        {
            await ProcessEntryAsync(db, services, entry, ct);
        }
    }
}
