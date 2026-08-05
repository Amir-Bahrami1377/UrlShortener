using System.Net.Http.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Shortener.Application.Abstractions;
using Shortener.Domain.Enums;

namespace Shortener.Infrastructure.SmsProviders;

/// <summary>
/// §M5.1 — real HTTP client for api.kavenegar.com, built from Kavenegar's long-stable public REST
/// contract (URL-embedded API key, GET-based send/status endpoints, a `return.status` envelope that
/// is 200 on success even though the HTTP transport status is always 200). No live account exists in
/// this sandbox — this has never been exercised against the real service, only against a mocked
/// HttpMessageHandler in tests. Treat as contract-untested until validated with real credentials.
/// </summary>
public sealed class KavenegarProvider(IHttpClientFactory httpClientFactory, ILogger<KavenegarProvider> logger) : ISmsProvider
{
    public string ProviderCode => "kavenegar";

    public IReadOnlyList<ProviderField> GetRequiredFields() =>
    [
        new ProviderField("ApiKey", "کلید API", IsRequired: true, IsSecret: true),
        new ProviderField("SenderNumber", "شماره فرستنده", IsRequired: true, IsSecret: false),
    ];

    public async Task<SmsSendResult> SendAsync(SmsAccountConfig config, SmsSendRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(config.ApiKey))
        {
            return Failure("ERR_CREDENTIALS_MISSING", "کلید API پیکربندی نشده است.", isRetryable: false);
        }

        var client = httpClientFactory.CreateClient(SmsHttpClientNames.Kavenegar);
        using var timeoutCts = new CancellationTokenSource(SmsProviderResilience.TimeoutFor(request.MessageType));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            var url = string.IsNullOrEmpty(request.PatternCode)
                ? QueryHelpers.AddQueryString($"/v1/{config.ApiKey}/sms/send.json", new Dictionary<string, string?>
                {
                    ["receptor"] = request.PhoneNumber,
                    ["message"] = request.Body,
                    ["sender"] = config.SenderNumber,
                })
                : QueryHelpers.AddQueryString($"/v1/{config.ApiKey}/verify/lookup.json", new Dictionary<string, string?>
                {
                    ["receptor"] = request.PhoneNumber,
                    ["template"] = request.PatternCode,
                    ["token"] = request.PatternTokens?.Values.FirstOrDefault() ?? request.Body,
                });

            using var response = await client.GetAsync(url, linkedCts.Token);
            var payload = await response.Content.ReadFromJsonAsync<KavenegarEnvelope>(linkedCts.Token);

            if (payload?.Return is null)
            {
                return Failure("ERR_PROVIDER_RESPONSE", "پاسخ نامعتبر از Kavenegar.", isRetryable: true);
            }

            if (payload.Return.Status != 200)
            {
                // 4xx-equivalent (auth/params/credit) are permanent; 5xx-equivalent provider outages are retryable.
                var retryable = payload.Return.Status is >= 500 and < 600;
                return Failure($"KAVENEGAR_{payload.Return.Status}", payload.Return.Message ?? "خطای نامشخص از Kavenegar.", retryable);
            }

            var entry = payload.Entries?.FirstOrDefault();
            return new SmsSendResult(
                IsSuccess: true,
                ProviderMessageId: entry?.MessageId.ToString(),
                ProviderStatusCode: entry?.Status.ToString(),
                ErrorMessage: null,
                IsRetryable: false,
                Cost: entry?.Cost);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Failure("ERR_PROVIDER_TIMEOUT", "پاسخ Kavenegar در زمان مجاز دریافت نشد.", isRetryable: true);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Kavenegar request failed (network)");
            return Failure("ERR_PROVIDER_UNREACHABLE", "ارتباط با Kavenegar برقرار نشد.", isRetryable: true);
        }
    }

    public async Task<IReadOnlyList<SmsDeliveryStatus>> GetStatusAsync(
        SmsAccountConfig config, IReadOnlyList<string> providerMessageIds, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(config.ApiKey) || providerMessageIds.Count == 0)
        {
            return [];
        }

        var client = httpClientFactory.CreateClient(SmsHttpClientNames.Kavenegar);
        var url = QueryHelpers.AddQueryString($"/v1/{config.ApiKey}/sms/status.json", "messageid", string.Join(",", providerMessageIds));

        try
        {
            using var response = await client.GetAsync(url, ct);
            var payload = await response.Content.ReadFromJsonAsync<KavenegarEnvelope>(ct);
            if (payload?.Return is not { Status: 200 } || payload.Entries is null)
            {
                return [];
            }

            return payload.Entries
                .Select(e => new SmsDeliveryStatus(e.MessageId.ToString(), MapStatus(e.Status), e.Status.ToString()))
                .ToList();
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Kavenegar status query failed (network)");
            return [];
        }
    }

    public async Task<decimal?> GetCreditAsync(SmsAccountConfig config, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(config.ApiKey))
        {
            return null;
        }

        var client = httpClientFactory.CreateClient(SmsHttpClientNames.Kavenegar);
        try
        {
            using var response = await client.GetAsync($"/v1/{config.ApiKey}/account/info.json", ct);
            var payload = await response.Content.ReadFromJsonAsync<KavenegarAccountEnvelope>(ct);
            return payload?.Return?.Status == 200 ? payload.Entries?.RemainCredit : null;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Kavenegar credit query failed (network)");
            return null;
        }
    }

    /// <summary>Kavenegar's delivery-status codes (best-effort from public docs — see class doc).</summary>
    private static SmsStatus MapStatus(int kavenegarStatus) => kavenegarStatus switch
    {
        1 or 2 or 4 => SmsStatus.Sent,       // queued / scheduled / sent to telecom
        6 or 11 => SmsStatus.Delivered,      // delivered / confirmed received
        5 or 10 or 13 or 14 or 15 or 16 => SmsStatus.Failed, // send error / blocked / unknown-number classes
        _ => SmsStatus.Sent,
    };

    private static SmsSendResult Failure(string code, string message, bool isRetryable) =>
        new(IsSuccess: false, ProviderMessageId: null, ProviderStatusCode: code, ErrorMessage: message, IsRetryable: isRetryable, Cost: null);

    private sealed record KavenegarEnvelope(KavenegarReturn? Return, IReadOnlyList<KavenegarEntry>? Entries);

    private sealed record KavenegarReturn(int Status, string? Message);

    private sealed record KavenegarEntry(long MessageId, int Status, decimal? Cost);

    private sealed record KavenegarAccountEnvelope(KavenegarReturn? Return, KavenegarAccountEntry? Entries);

    private sealed record KavenegarAccountEntry(decimal RemainCredit);
}
