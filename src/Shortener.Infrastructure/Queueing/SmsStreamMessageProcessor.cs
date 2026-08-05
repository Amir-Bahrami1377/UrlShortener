using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shortener.Application.Abstractions;
using Shortener.Domain.Entities;
using Shortener.Domain.Enums;
using Shortener.Infrastructure.Observability;
using Shortener.Infrastructure.Persistence;

namespace Shortener.Infrastructure.Queueing;

public enum SmsProcessOutcome
{
    /// <summary>Unknown id, already Sent/Failed/etc., or already carries a ProviderMessageId — ack and move on.</summary>
    AlreadyHandled,
    Sent,
    /// <summary>Retryable failure under the attempt cap — caller must NOT ack; XAUTOCLAIM redelivers it later.</summary>
    RetryLater,
    /// <summary>Non-retryable, or the attempt cap was reached — caller XADDs to the dead stream, then acks.</summary>
    PermanentlyFailed,
}

/// <summary>
/// §M5.3 steps 1-8 — the per-message send algorithm, identical for both streams (they differ only
/// in batch size, rate limiting, and consumer group, all handled by the caller). Shared by
/// OtpSmsWorker and BulkSmsWorker so the algorithm exists in exactly one place.
/// </summary>
public sealed class SmsStreamMessageProcessor(
    AppDbContext db,
    ISmsProviderFactory providerFactory,
    ICredentialProtector credentialProtector,
    IOptions<QueueOptions> queueOptions,
    ShortenerMetrics metrics,
    ILogger<SmsStreamMessageProcessor> logger)
{
    private readonly QueueOptions _queue = queueOptions.Value;

    public async Task<SmsProcessOutcome> ProcessAsync(long smsMessageId, CancellationToken ct)
    {
        var message = await db.SmsMessages
            .Include(m => m.SmsAccount)
            .ThenInclude(a => a!.SmsProvider)
            .FirstOrDefaultAsync(m => m.Id == smsMessageId, ct);

        if (message is null || message.Status != SmsStatus.Queued || !string.IsNullOrEmpty(message.ProviderMessageId))
        {
            return SmsProcessOutcome.AlreadyHandled;
        }

        message.Status = SmsStatus.Sending;
        await db.SaveChangesAsync(ct);

        var account = message.SmsAccount!;
        var config = new SmsAccountConfig(
            ApiKey: credentialProtector.Unprotect(account.ApiKeyEnc),
            Username: credentialProtector.Unprotect(account.UsernameEnc),
            Password: credentialProtector.Unprotect(account.PasswordEnc),
            SenderNumber: account.SenderNumber,
            BaseUrl: account.BaseUrl,
            SettingsJson: account.SettingsJson);
        var request = new SmsSendRequest(message.PhoneNumber, message.Body, PatternCode: null, PatternTokens: null, message.MessageType);

        SmsSendResult result;
        try
        {
            var provider = providerFactory.Resolve(account.SmsProvider!.Code);
            result = await provider.SendAsync(config, request, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Unhandled exception sending SmsMessage {Id} via {Provider}", message.Id, account.SmsProvider?.Code);
            result = new SmsSendResult(false, null, "ERR_PROVIDER_EXCEPTION", ex.Message, IsRetryable: true, null);
        }

        if (result.IsSuccess)
        {
            message.Status = SmsStatus.Sent;
            message.ProviderMessageId = result.ProviderMessageId;
            message.SentAt = DateTime.UtcNow;
            message.Cost = result.Cost;
            db.SmsStatusHistories.Add(new SmsStatusHistory
            {
                SmsMessageId = message.Id,
                Status = SmsStatus.Sent,
                ProviderStatusCode = result.ProviderStatusCode,
            });
            await db.SaveChangesAsync(ct);
            metrics.SmsSent(message.MessageType.ToString(), account.SmsProvider!.Code, "success");
            return SmsProcessOutcome.Sent;
        }

        if (result.IsRetryable && message.TryCount + 1 < _queue.MaxDeliveryAttempts)
        {
            message.Status = SmsStatus.Queued;
            message.TryCount += 1;
            message.NextRetryAt = DateTime.UtcNow.AddMinutes(Math.Pow(2, message.TryCount));
            message.LastError = result.ErrorMessage;
            await db.SaveChangesAsync(ct);
            return SmsProcessOutcome.RetryLater;
        }

        message.Status = SmsStatus.Failed;
        message.TryCount += 1;
        message.LastError = result.ErrorMessage;
        db.SmsStatusHistories.Add(new SmsStatusHistory
        {
            SmsMessageId = message.Id,
            Status = SmsStatus.Failed,
            ProviderStatusCode = result.ProviderStatusCode,
            Description = result.ErrorMessage,
        });
        await db.SaveChangesAsync(ct);
        metrics.SmsSent(message.MessageType.ToString(), account.SmsProvider?.Code ?? "unknown", "failed");
        return SmsProcessOutcome.PermanentlyFailed;
    }
}
