using Shortener.Domain.Enums;

namespace Shortener.Application.Abstractions;

public interface ISmsProvider
{
    string ProviderCode { get; }

    IReadOnlyList<ProviderField> GetRequiredFields();

    Task<SmsSendResult> SendAsync(SmsAccountConfig config, SmsSendRequest request, CancellationToken ct);

    Task<IReadOnlyList<SmsDeliveryStatus>> GetStatusAsync(
        SmsAccountConfig config, IReadOnlyList<string> providerMessageIds, CancellationToken ct);

    Task<decimal?> GetCreditAsync(SmsAccountConfig config, CancellationToken ct);
}

/// <summary>Resolves the configured ISmsProvider for a given provider code (keyed DI lookup).</summary>
public interface ISmsProviderFactory
{
    ISmsProvider Resolve(string providerCode);
}

public sealed record ProviderField(string Key, string Label, bool IsRequired, bool IsSecret);

/// <summary>Decrypted account configuration handed to a provider at send time. Never logged.</summary>
public sealed record SmsAccountConfig(
    string? ApiKey,
    string? Username,
    string? Password,
    string? SenderNumber,
    string? BaseUrl,
    string? SettingsJson);

public sealed record SmsSendRequest(
    string PhoneNumber,
    string Body,
    string? PatternCode,
    IReadOnlyDictionary<string, string>? PatternTokens,
    SmsMessageType MessageType);

public sealed record SmsSendResult(
    bool IsSuccess,
    string? ProviderMessageId,
    string? ProviderStatusCode,
    string? ErrorMessage,
    bool IsRetryable,
    decimal? Cost);

public sealed record SmsDeliveryStatus(string ProviderMessageId, SmsStatus Status, string? ProviderStatusCode);
