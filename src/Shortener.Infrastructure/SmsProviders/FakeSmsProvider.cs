using Microsoft.Extensions.Logging;
using Shortener.Application.Abstractions;

namespace Shortener.Infrastructure.SmsProviders;

/// <summary>
/// Development-only stand-in for a real SMS gateway: logs the message body (which is how you read
/// an OTP code during local testing) and always succeeds. Registered only when IHostEnvironment.IsDevelopment().
/// This is the one deliberate exception to §M8.1's "never log phone number/OTP" rule — the doc's own
/// M5.1 spec requires it ("FakeSmsProvider logs the OTP code and message text at Information level"),
/// it never runs outside Development, and without it there would be no way to read a test OTP at all.
/// </summary>
public sealed class FakeSmsProvider(ILogger<FakeSmsProvider> logger) : ISmsProvider
{
    public string ProviderCode => "fake";

    public IReadOnlyList<ProviderField> GetRequiredFields() => [];

    public Task<SmsSendResult> SendAsync(SmsAccountConfig config, SmsSendRequest request, CancellationToken ct)
    {
        logger.LogInformation(
            "[FakeSmsProvider] To {PhoneNumber} ({MessageType}): {Body}",
            request.PhoneNumber, request.MessageType, request.Body);

        var providerMessageId = $"fake-{Guid.NewGuid():N}";
        return Task.FromResult(new SmsSendResult(
            IsSuccess: true,
            ProviderMessageId: providerMessageId,
            ProviderStatusCode: "OK",
            ErrorMessage: null,
            IsRetryable: false,
            Cost: 0m));
    }

    public Task<IReadOnlyList<SmsDeliveryStatus>> GetStatusAsync(
        SmsAccountConfig config, IReadOnlyList<string> providerMessageIds, CancellationToken ct)
    {
        IReadOnlyList<SmsDeliveryStatus> statuses = providerMessageIds
            .Select(id => new SmsDeliveryStatus(id, Domain.Enums.SmsStatus.Delivered, "OK"))
            .ToList();
        return Task.FromResult(statuses);
    }

    public Task<decimal?> GetCreditAsync(SmsAccountConfig config, CancellationToken ct) =>
        Task.FromResult<decimal?>(1_000_000m);
}
