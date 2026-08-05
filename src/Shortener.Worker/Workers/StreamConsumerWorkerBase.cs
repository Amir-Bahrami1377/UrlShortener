using Shortener.Infrastructure.Queueing;
using StackExchange.Redis;

namespace Shortener.Worker.Workers;

/// <summary>
/// §M5.3's shared consumer-loop skeleton: consumer-group bootstrap, XREADGROUP polling, and the
/// per-entry XACK/dead-letter bookkeeping that's identical for both streams. OtpSmsWorker and
/// BulkSmsWorker each supply their own stream name, group, batch size, and (for Bulk) a rate-limit
/// gate — the actual per-message send algorithm lives in SmsStreamMessageProcessor, not here.
/// </summary>
public abstract class StreamConsumerWorkerBase(
    IConnectionMultiplexer redis, IServiceScopeFactory scopeFactory, ILogger logger) : BackgroundService
{
    protected abstract string Stream { get; }
    protected abstract string ConsumerGroup { get; }
    protected abstract string DeadStream { get; }
    protected abstract int BatchSize { get; }
    protected abstract string ConsumerNamePrefix { get; }

    private string ConsumerName => $"{ConsumerNamePrefix}-{Environment.MachineName}-{Environment.ProcessId}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var db = redis.GetDatabase();
        await EnsureConsumerGroupAsync(db);

        while (!stoppingToken.IsCancellationRequested)
        {
            StreamEntry[] entries;
            try
            {
                entries = await db.StreamReadGroupAsync(Stream, ConsumerGroup, ConsumerName, ">", count: BatchSize);
            }
            catch (RedisConnectionException ex)
            {
                logger.LogError(ex, "Redis unavailable while reading {Stream}; retrying shortly", Stream);
                await Task.Delay(2000, stoppingToken);
                continue;
            }

            if (entries.Length == 0)
            {
                await Task.Delay(500, stoppingToken);
                continue;
            }

            using var scope = scopeFactory.CreateScope();
            await ProcessBatchAsync(db, scope.ServiceProvider, entries, stoppingToken);
        }
    }

    protected abstract Task ProcessBatchAsync(IDatabase db, IServiceProvider services, StreamEntry[] entries, CancellationToken ct);

    /// <summary>Returns false only for RetryLater (caller must not ack) — see SmsStreamEntryHandler.</summary>
    protected Task<bool> ProcessEntryAsync(IDatabase db, IServiceProvider services, StreamEntry entry, CancellationToken ct) =>
        services.GetRequiredService<SmsStreamEntryHandler>().HandleAsync(db, services, Stream, ConsumerGroup, DeadStream, entry, ct);

    private async Task EnsureConsumerGroupAsync(IDatabase db)
    {
        try
        {
            await db.StreamCreateConsumerGroupAsync(Stream, ConsumerGroup, "0-0", createStream: true);
        }
        catch (RedisServerException ex) when (ex.Message.Contains("BUSYGROUP"))
        {
        }
    }
}
