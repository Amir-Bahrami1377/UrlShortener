using Shortener.Domain.Enums;

namespace Shortener.Application.Abstractions;

/// <summary>
/// Reports the apparent SMS status of a ShortLink's DownloadLink message: the real SmsMessage.Status
/// once the Outbox publisher has resolved it into one, or Queued/Pending while it's still in flight
/// as an OutboxMessage intent (§M5.2) that hasn't been resolved into a SmsMessage yet.
/// </summary>
public interface ILinkSmsStatusResolver
{
    Task<SmsStatus> GetStatusAsync(long shortLinkId, CancellationToken ct);

    Task<HashSet<long>> GetDispatchedLinkIdsAsync(CancellationToken ct);
}
