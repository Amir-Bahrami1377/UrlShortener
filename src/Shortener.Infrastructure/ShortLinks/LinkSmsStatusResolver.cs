using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Shortener.Application.Abstractions;
using Shortener.Application.Contracts;
using Shortener.Domain.Enums;
using Shortener.Infrastructure.Persistence;

namespace Shortener.Infrastructure.ShortLinks;

public sealed class LinkSmsStatusResolver(AppDbContext db) : ILinkSmsStatusResolver
{
    public async Task<SmsStatus> GetStatusAsync(long shortLinkId, CancellationToken ct)
    {
        var realStatus = await db.SmsMessages
            .Where(m => m.ShortLinkId == shortLinkId && m.MessageType == SmsMessageType.DownloadLink)
            .OrderByDescending(m => m.CreatedAt)
            .Select(m => (SmsStatus?)m.Status)
            .FirstOrDefaultAsync(ct);

        if (realStatus is { } status)
        {
            return status;
        }

        var inFlight = await GetDispatchedLinkIdsAsync(ct);
        return inFlight.Contains(shortLinkId) ? SmsStatus.Queued : SmsStatus.Pending;
    }

    /// <summary>"Dispatched" = has an in-flight LinkSms OutboxMessage (not yet resolved into a
    /// SmsMessage) OR already has a real SmsMessage. Outbox rows that permanently failed (Status =
    /// Failed) are excluded so a fresh dispatch-sms call can retry after a config fix.</summary>
    public async Task<HashSet<long>> GetDispatchedLinkIdsAsync(CancellationToken ct)
    {
        var payloads = await db.OutboxMessages
            .Where(o => o.Type == OutboxMessageTypes.LinkSms && o.Status != OutboxStatus.Failed)
            .Select(o => o.PayloadJson)
            .ToListAsync(ct);

        var inFlightIds = payloads
            .Select(json => JsonSerializer.Deserialize<LinkSmsPayload>(json))
            .Where(p => p is not null)
            .Select(p => p!.ShortLinkId)
            .ToHashSet();

        var alreadySentIds = await db.SmsMessages
            .Where(m => m.MessageType == SmsMessageType.DownloadLink)
            .Select(m => m.ShortLinkId!.Value)
            .ToListAsync(ct);

        inFlightIds.UnionWith(alreadySentIds);
        return inFlightIds;
    }
}
