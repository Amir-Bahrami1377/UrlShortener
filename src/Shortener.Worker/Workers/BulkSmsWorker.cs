using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shortener.Infrastructure.Persistence;
using Shortener.Infrastructure.Queueing;
using StackExchange.Redis;

namespace Shortener.Worker.Workers;

/// <summary>
/// §M5.3 — the bulk consumer: batch size 100, per-SmsAccount RatePerMinute enforced via a Redis
/// per-minute counter, target latency &lt; 30 minutes. An entry whose account is already at its
/// per-minute cap is simply left unacked for this tick rather than delayed in-process — the next
/// XREADGROUP poll (500ms later) or the XAUTOCLAIM claimer picks it back up once the window rolls over.
/// </summary>
public sealed class BulkSmsWorker(
    IConnectionMultiplexer redis,
    IServiceScopeFactory scopeFactory,
    IOptions<QueueOptions> queueOptions,
    ILogger<BulkSmsWorker> logger)
    : StreamConsumerWorkerBase(redis, scopeFactory, logger)
{
    private readonly QueueOptions _options = queueOptions.Value;

    protected override string Stream => _options.BulkStream;
    protected override string ConsumerGroup => _options.BulkConsumerGroup;
    protected override string DeadStream => _options.DeadStream;
    protected override int BatchSize => 100;
    protected override string ConsumerNamePrefix => "bulk";

    protected override async Task ProcessBatchAsync(IDatabase db, IServiceProvider services, StreamEntry[] entries, CancellationToken ct)
    {
        var dbContext = services.GetRequiredService<AppDbContext>();

        var idByEntry = new Dictionary<StreamEntry, long>();
        foreach (var entry in entries)
        {
            var idField = entry.Values.FirstOrDefault(v => v.Name == "smsMessageId");
            if (!idField.Name.IsNullOrEmpty && long.TryParse(idField.Value.ToString(), out var id))
            {
                idByEntry[entry] = id;
            }
        }

        var smsMessageIds = idByEntry.Values.ToList();
        var accountByMessage = await dbContext.SmsMessages
            .Where(m => smsMessageIds.Contains(m.Id))
            .Select(m => new { m.Id, m.SmsAccountId, RatePerMinute = m.SmsAccount!.RatePerMinute })
            .ToDictionaryAsync(x => x.Id, x => (x.SmsAccountId, x.RatePerMinute), ct);

        foreach (var entry in entries)
        {
            if (!idByEntry.TryGetValue(entry, out var smsMessageId))
            {
                await ProcessEntryAsync(db, services, entry, ct); // malformed field — let the base ack/skip it
                continue;
            }

            if (accountByMessage.TryGetValue(smsMessageId, out var account) &&
                !await TryReserveRateSlotAsync(db, account.SmsAccountId, account.RatePerMinute))
            {
                continue; // over cap this minute — left unacked, retried once the window rolls over
            }

            await ProcessEntryAsync(db, services, entry, ct);
        }
    }

    private static async Task<bool> TryReserveRateSlotAsync(IDatabase db, int smsAccountId, int ratePerMinute)
    {
        var bucket = DateTime.UtcNow.ToString("yyyyMMddHHmm");
        var key = $"ratelimit:sms:{smsAccountId}:{bucket}";
        var count = await db.StringIncrementAsync(key);
        if (count == 1)
        {
            await db.KeyExpireAsync(key, TimeSpan.FromSeconds(120));
        }

        if (count > ratePerMinute)
        {
            await db.StringDecrementAsync(key);
            return false;
        }

        return true;
    }
}
