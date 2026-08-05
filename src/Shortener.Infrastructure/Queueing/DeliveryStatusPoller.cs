using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shortener.Application.Abstractions;
using Shortener.Domain.Entities;
using Shortener.Domain.Enums;
using Shortener.Infrastructure.Persistence;

namespace Shortener.Infrastructure.Queueing;

/// <summary>§M5.6, factored out of DeliveryStatusPollerService so a single poll pass is directly
/// testable without a running BackgroundService.</summary>
public sealed class DeliveryStatusPoller(
    AppDbContext db,
    ISmsProviderFactory providerFactory,
    ICredentialProtector credentialProtector,
    ILogger<DeliveryStatusPoller> logger)
{
    private const int ProviderBatchSize = 100;

    public async Task<int> PollOnceAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var candidates = await db.SmsMessages
            .Include(m => m.SmsAccount)
            .ThenInclude(a => a!.SmsProvider)
            .Where(m => m.Status == SmsStatus.Sent && m.ProviderMessageId != null &&
                        m.SentAt != null && m.SentAt < now.AddMinutes(-1) && m.SentAt > now.AddHours(-24))
            .ToListAsync(ct);

        var updated = 0;
        foreach (var group in candidates.GroupBy(m => m.SmsAccountId))
        {
            var account = group.First().SmsAccount;
            if (account?.SmsProvider is null)
            {
                continue;
            }

            var config = new SmsAccountConfig(
                ApiKey: credentialProtector.Unprotect(account.ApiKeyEnc),
                Username: credentialProtector.Unprotect(account.UsernameEnc),
                Password: credentialProtector.Unprotect(account.PasswordEnc),
                SenderNumber: account.SenderNumber,
                BaseUrl: account.BaseUrl,
                SettingsJson: account.SettingsJson);

            ISmsProvider provider;
            try
            {
                provider = providerFactory.Resolve(account.SmsProvider.Code);
            }
            catch (Exception ex)
            {
                // One account with a bad/unregistered provider code must not stop the rest of this
                // poll pass from checking every other account's deliveries.
                logger.LogWarning(ex, "DLR poll: could not resolve provider '{ProviderCode}' for SmsAccount {SmsAccountId}", account.SmsProvider.Code, group.Key);
                continue;
            }

            foreach (var chunk in group.Chunk(ProviderBatchSize))
            {
                var messageByProviderId = chunk.ToDictionary(m => m.ProviderMessageId!);

                IReadOnlyList<SmsDeliveryStatus> statuses;
                try
                {
                    statuses = await provider.GetStatusAsync(config, messageByProviderId.Keys.ToList(), ct);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "DLR poll failed for SmsAccount {SmsAccountId}", group.Key);
                    continue;
                }

                foreach (var status in statuses)
                {
                    if (!messageByProviderId.TryGetValue(status.ProviderMessageId, out var message))
                    {
                        continue;
                    }

                    message.StatusCheckedAt = DateTime.UtcNow;

                    if (status.Status == message.Status)
                    {
                        continue;
                    }

                    message.Status = status.Status;
                    if (status.Status == SmsStatus.Delivered)
                    {
                        message.DeliveredAt = DateTime.UtcNow;
                    }

                    db.SmsStatusHistories.Add(new SmsStatusHistory
                    {
                        SmsMessageId = message.Id,
                        Status = status.Status,
                        ProviderStatusCode = status.ProviderStatusCode,
                    });
                    updated++;
                }

                await db.SaveChangesAsync(ct);
            }
        }

        var undeliveredCount = await db.SmsMessages
            .Where(m => m.Status == SmsStatus.Sent && m.SentAt != null && m.SentAt < now.AddHours(-24))
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Status, SmsStatus.Undelivered)
                .SetProperty(m => m.StatusCheckedAt, now), ct);

        if (undeliveredCount > 0)
        {
            logger.LogInformation("DLR poll: marked {Count} SmsMessages Undelivered after 24h with no delivery confirmation", undeliveredCount);
        }

        return updated + undeliveredCount;
    }
}
