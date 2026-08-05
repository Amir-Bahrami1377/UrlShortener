using Shortener.Application.Abstractions;
using Shortener.Domain.Enums;

namespace Shortener.IntegrationTests;

/// <summary>
/// Deterministic ISmsProvider double for exercising SmsStreamMessageProcessor's retry/dead-letter
/// paths, which FakeSmsProvider (always succeeds) can't reach. Behavior is selected per-call via
/// SmsAccountConfig.SettingsJson ("retryable" | "permanent" | anything else = success) — read from
/// the SmsAccount row each test creates, so tests running in parallel never share mutable state.
/// </summary>
public sealed class TestControlledSmsProvider : ISmsProvider
{
    public const string Code = "test-controlled";
    public const string Retryable = "retryable";
    public const string Permanent = "permanent";

    public string ProviderCode => Code;

    /// <summary>The request from the most recent SendAsync call — safe to inspect since this provider
    /// is registered Scoped and each test resolves its own scope.</summary>
    public SmsSendRequest? LastRequest { get; private set; }

    public IReadOnlyList<ProviderField> GetRequiredFields() => [];

    public Task<SmsSendResult> SendAsync(SmsAccountConfig config, SmsSendRequest request, CancellationToken ct)
    {
        LastRequest = request;
        return Task.FromResult(config.SettingsJson switch
        {
            Retryable => new SmsSendResult(false, null, "TEST_RETRYABLE", "simulated retryable failure", IsRetryable: true, Cost: null),
            Permanent => new SmsSendResult(false, null, "TEST_PERMANENT", "simulated permanent failure", IsRetryable: false, Cost: null),
            _ => new SmsSendResult(true, $"test-{Guid.NewGuid():N}", "OK", null, IsRetryable: false, Cost: 0m),
        });
    }

    public Task<IReadOnlyList<SmsDeliveryStatus>> GetStatusAsync(
        SmsAccountConfig config, IReadOnlyList<string> providerMessageIds, CancellationToken ct)
    {
        IReadOnlyList<SmsDeliveryStatus> statuses = providerMessageIds
            .Select(id => new SmsDeliveryStatus(id, SmsStatus.Delivered, "OK"))
            .ToList();
        return Task.FromResult(statuses);
    }

    public Task<decimal?> GetCreditAsync(SmsAccountConfig config, CancellationToken ct) => Task.FromResult<decimal?>(1000m);
}
