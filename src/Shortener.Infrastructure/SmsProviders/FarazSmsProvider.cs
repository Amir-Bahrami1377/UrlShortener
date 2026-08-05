using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Shortener.Application.Abstractions;
using Shortener.Domain.Enums;

namespace Shortener.Infrastructure.SmsProviders;

/// <summary>
/// §M5.1 — real HTTP client for FarazSMS/ippanel's "edge" REST API (Bearer-style AccessKey auth,
/// JSON POST). Confidence in this exact contract is lower than Kavenegar/MeliPayamak — ippanel has
/// shipped multiple API generations over the years and no live account exists here to validate
/// against. Structurally correct against ISmsProvider and unit-tested against a mocked
/// HttpMessageHandler, but must be re-verified against the provider's current docs and a real
/// account before production use.
/// </summary>
public sealed class FarazSmsProvider(IHttpClientFactory httpClientFactory, ILogger<FarazSmsProvider> logger) : ISmsProvider
{
    public string ProviderCode => "farazsms";

    public IReadOnlyList<ProviderField> GetRequiredFields() =>
    [
        new ProviderField("ApiKey", "کلید دسترسی (AccessKey)", IsRequired: true, IsSecret: true),
        new ProviderField("SenderNumber", "شماره فرستنده", IsRequired: true, IsSecret: false),
    ];

    public async Task<SmsSendResult> SendAsync(SmsAccountConfig config, SmsSendRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(config.ApiKey))
        {
            return Failure("ERR_CREDENTIALS_MISSING", "کلید دسترسی پیکربندی نشده است.", isRetryable: false);
        }

        var client = httpClientFactory.CreateClient(SmsHttpClientNames.FarazSms);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("AccessKey", config.ApiKey);

        using var timeoutCts = new CancellationTokenSource(SmsProviderResilience.TimeoutFor(request.MessageType));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            HttpResponseMessage response;
            if (!string.IsNullOrEmpty(request.PatternCode))
            {
                response = await client.PostAsJsonAsync("/v1/api/send/pattern", new
                {
                    code = request.PatternCode,
                    sender = config.SenderNumber,
                    recipient = request.PhoneNumber,
                    variable = request.PatternTokens ?? new Dictionary<string, string>(),
                }, linkedCts.Token);
            }
            else
            {
                response = await client.PostAsJsonAsync("/v1/api/send", new
                {
                    originator = config.SenderNumber,
                    recipients = new[] { request.PhoneNumber },
                    message = request.Body,
                }, linkedCts.Token);
            }

            using (response)
            {
                var payload = await response.Content.ReadFromJsonAsync<FarazSmsResponse>(linkedCts.Token);
                if (payload is null)
                {
                    return Failure("ERR_PROVIDER_RESPONSE", "پاسخ نامعتبر از FarazSMS.", isRetryable: true);
                }

                if (!string.Equals(payload.Status, "OK", StringComparison.OrdinalIgnoreCase))
                {
                    var retryable = (int)response.StatusCode is >= 500 and < 600;
                    return Failure("FARAZSMS_ERROR", payload.ErrorMessage ?? "خطای نامشخص از FarazSMS.", retryable);
                }

                return new SmsSendResult(
                    IsSuccess: true,
                    ProviderMessageId: payload.Data?.MessageId,
                    ProviderStatusCode: "OK",
                    ErrorMessage: null,
                    IsRetryable: false,
                    Cost: payload.Data?.Cost);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Failure("ERR_PROVIDER_TIMEOUT", "پاسخ FarazSMS در زمان مجاز دریافت نشد.", isRetryable: true);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "FarazSMS request failed (network)");
            return Failure("ERR_PROVIDER_UNREACHABLE", "ارتباط با FarazSMS برقرار نشد.", isRetryable: true);
        }
    }

    public async Task<IReadOnlyList<SmsDeliveryStatus>> GetStatusAsync(
        SmsAccountConfig config, IReadOnlyList<string> providerMessageIds, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(config.ApiKey) || providerMessageIds.Count == 0)
        {
            return [];
        }

        var client = httpClientFactory.CreateClient(SmsHttpClientNames.FarazSms);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("AccessKey", config.ApiKey);

        try
        {
            using var response = await client.PostAsJsonAsync(
                "/v1/api/status", new { message_ids = providerMessageIds }, ct);
            var payload = await response.Content.ReadFromJsonAsync<FarazSmsStatusResponse>(ct);
            if (payload?.Data is null)
            {
                return [];
            }

            return payload.Data
                .Select(e => new SmsDeliveryStatus(e.MessageId, MapStatus(e.Status), e.Status))
                .ToList();
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "FarazSMS status query failed (network)");
            return [];
        }
    }

    public async Task<decimal?> GetCreditAsync(SmsAccountConfig config, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(config.ApiKey))
        {
            return null;
        }

        var client = httpClientFactory.CreateClient(SmsHttpClientNames.FarazSms);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("AccessKey", config.ApiKey);

        try
        {
            using var response = await client.GetAsync("/v1/api/credit", ct);
            var payload = await response.Content.ReadFromJsonAsync<FarazSmsCreditResponse>(ct);
            return string.Equals(payload?.Status, "OK", StringComparison.OrdinalIgnoreCase) ? payload?.Credit : null;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "FarazSMS credit query failed (network)");
            return null;
        }
    }

    private static SmsStatus MapStatus(string? status) => status?.ToLowerInvariant() switch
    {
        "delivered" => SmsStatus.Delivered,
        "failed" or "rejected" or "undelivered" => SmsStatus.Failed,
        _ => SmsStatus.Sent,
    };

    private static SmsSendResult Failure(string code, string message, bool isRetryable) =>
        new(IsSuccess: false, ProviderMessageId: null, ProviderStatusCode: code, ErrorMessage: message, IsRetryable: isRetryable, Cost: null);

    private sealed record FarazSmsResponse(string? Status, string? ErrorMessage, FarazSmsData? Data);

    private sealed record FarazSmsData([property: JsonPropertyName("message_id")] string? MessageId, decimal? Cost);

    private sealed record FarazSmsStatusResponse(IReadOnlyList<FarazSmsStatusEntry>? Data);

    private sealed record FarazSmsStatusEntry([property: JsonPropertyName("message_id")] string MessageId, string? Status);

    private sealed record FarazSmsCreditResponse(string? Status, decimal? Credit);
}
