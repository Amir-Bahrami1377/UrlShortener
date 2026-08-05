using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shortener.Application.Contracts;
using Shortener.Application.Services;
using Shortener.Domain.Entities;
using Shortener.Domain.Enums;
using Shortener.Infrastructure.Persistence;
using Shortener.Infrastructure.ShortLinks;
using StackExchange.Redis;

namespace Shortener.Infrastructure.Queueing;

public enum LinkSmsResolveOutcome
{
    /// <summary>Sent (or the link/SmsMessage already existed) — the Outbox row can be considered done.</summary>
    Done,
    /// <summary>Transient failure (misconfiguration or Redis) — caller should revert the Outbox row to Pending.</summary>
    Failed,
}

/// <summary>
/// §M5.2's actual publish step, factored out of OutboxPublisherService so it's directly testable
/// without a running BackgroundService: resolves a LinkSms intent (just a ShortLinkId) into a real
/// SmsMessage — Bulk-purpose SmsAccount + DownloadLink template (falling back from an exact ReportId
/// match to the client's global template), rendered body — and XADDs it onto sms:bulk.
/// </summary>
public sealed class LinkSmsOutboxResolver(
    AppDbContext db,
    IConnectionMultiplexer redis,
    IOptions<QueueOptions> queueOptions,
    IOptions<ShortLinkOptions> shortLinkOptions,
    ILogger<LinkSmsOutboxResolver> logger)
{
    private readonly QueueOptions _queue = queueOptions.Value;
    private readonly ShortLinkOptions _shortLink = shortLinkOptions.Value;

    public async Task<(LinkSmsResolveOutcome Outcome, string? Error)> ResolveAsync(string payloadJson, CancellationToken ct)
    {
        LinkSmsPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<LinkSmsPayload>(payloadJson);
        }
        catch (JsonException)
        {
            return (LinkSmsResolveOutcome.Failed, "Invalid LinkSms payload JSON");
        }

        if (payload is null)
        {
            return (LinkSmsResolveOutcome.Failed, "Empty LinkSms payload");
        }

        var link = await db.ShortLinks.FirstOrDefaultAsync(l => l.Id == payload.ShortLinkId, ct);
        if (link is null)
        {
            logger.LogWarning("LinkSms outbox entry references ShortLink {ShortLinkId} which no longer exists — skipping", payload.ShortLinkId);
            return (LinkSmsResolveOutcome.Done, null);
        }

        var alreadySent = await db.SmsMessages.AnyAsync(
            m => m.ShortLinkId == link.Id && m.MessageType == SmsMessageType.DownloadLink, ct);
        if (alreadySent)
        {
            return (LinkSmsResolveOutcome.Done, null);
        }

        var smsAccount = await db.SmsAccounts
            .Where(a => a.ClientId == link.ClientId && a.Purpose == SmsAccountPurpose.Bulk && a.IsActive)
            .OrderByDescending(a => a.IsDefault)
            .FirstOrDefaultAsync(ct);

        var template = await db.MessageTemplates
            .Where(t => t.ClientId == link.ClientId && t.TemplateType == TemplateType.DownloadLink && t.IsActive &&
                        (t.ReportId == link.ReportId || t.ReportId == null))
            .OrderByDescending(t => t.ReportId == link.ReportId)
            .FirstOrDefaultAsync(ct);

        if (smsAccount is null || template is null)
        {
            logger.LogWarning(
                "Bulk SMS not configured for Client {ClientId}: account={HasAccount}, template={HasTemplate}",
                link.ClientId, smsAccount is not null, template is not null);
            return (LinkSmsResolveOutcome.Failed, "ERR_BULK_SMS_NOT_CONFIGURED");
        }

        var body = TemplateRenderer.Render(template.Body, new Dictionary<string, string>
        {
            ["shortUrl"] = $"{_shortLink.BaseUrl.TrimEnd('/')}/s/{link.Code}",
            ["code"] = link.Code,
            ["reportName"] = link.ReportName,
            ["shop"] = link.Shop,
            ["shod"] = link.Shod,
            ["radif"] = link.Radif,
            ["expireDate"] = PersianDateHelper.ToPersianDate(link.ExpiresAt),
        });

        var smsMessage = new SmsMessage
        {
            ShortLinkId = link.Id,
            SmsAccountId = smsAccount.Id,
            TemplateId = template.Id,
            MessageType = SmsMessageType.DownloadLink,
            PhoneNumber = link.PhoneNumber,
            Body = body,
            Status = SmsStatus.Queued,
        };
        db.SmsMessages.Add(smsMessage);
        await db.SaveChangesAsync(ct);

        try
        {
            await redis.GetDatabase().StreamAddAsync(_queue.BulkStream, "smsMessageId", smsMessage.Id);
        }
        catch (RedisException ex)
        {
            // Compensate: an orphaned Queued row here would look "already sent" to the idempotency
            // check above on the next retry and would never actually get published.
            db.SmsMessages.Remove(smsMessage);
            await db.SaveChangesAsync(ct);
            return (LinkSmsResolveOutcome.Failed, $"ERR_REDIS_UNAVAILABLE: {ex.Message}");
        }

        return (LinkSmsResolveOutcome.Done, null);
    }
}
