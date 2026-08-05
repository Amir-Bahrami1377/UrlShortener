using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Shortener.Application.Abstractions;
using Shortener.Domain.Enums;

namespace Shortener.Infrastructure.SmsProviders;

/// <summary>
/// §M5.1 — real HTTP client for rest.payamak-panel.com (MeliPayamak), built from its public REST
/// contract (username/password auth in a JSON POST body, RetStatus == 1 meaning success). No live
/// account exists in this sandbox — never exercised against the real service, only against a mocked
/// HttpMessageHandler in tests. Treat as contract-untested until validated with real credentials.
/// Only RetStatus == 1 is confidently known to mean success; every other code is treated as a
/// permanent failure rather than guessing at a full status table.
/// </summary>
public sealed class MeliPayamakProvider(IHttpClientFactory httpClientFactory, ILogger<MeliPayamakProvider> logger) : ISmsProvider
{
    public string ProviderCode => "melipayamak";

    public IReadOnlyList<ProviderField> GetRequiredFields() =>
    [
        new ProviderField("Username", "نام کاربری", IsRequired: true, IsSecret: false),
        new ProviderField("Password", "رمز عبور", IsRequired: true, IsSecret: true),
        new ProviderField("SenderNumber", "شماره فرستنده", IsRequired: true, IsSecret: false),
    ];

    public async Task<SmsSendResult> SendAsync(SmsAccountConfig config, SmsSendRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(config.Username) || string.IsNullOrWhiteSpace(config.Password))
        {
            return Failure("ERR_CREDENTIALS_MISSING", "نام کاربری یا رمز عبور پیکربندی نشده است.", isRetryable: false);
        }

        var client = httpClientFactory.CreateClient(SmsHttpClientNames.MeliPayamak);
        using var timeoutCts = new CancellationTokenSource(SmsProviderResilience.TimeoutFor(request.MessageType));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            HttpResponseMessage response;
            if (!string.IsNullOrEmpty(request.PatternCode))
            {
                response = await client.PostAsJsonAsync("/api/SendSMS/SendByBaseNumber", new
                {
                    username = config.Username,
                    password = config.Password,
                    text = string.Join("|", request.PatternTokens?.Values ?? []),
                    to = request.PhoneNumber,
                    bodyId = request.PatternCode,
                }, linkedCts.Token);
            }
            else
            {
                response = await client.PostAsJsonAsync("/api/SendSMS/SendSMS", new
                {
                    username = config.Username,
                    password = config.Password,
                    to = request.PhoneNumber,
                    from = config.SenderNumber,
                    text = request.Body,
                    isFlash = false,
                }, linkedCts.Token);
            }

            using (response)
            {
                var payload = await response.Content.ReadFromJsonAsync<MeliPayamakResponse>(linkedCts.Token);
                if (payload is null)
                {
                    return Failure("ERR_PROVIDER_RESPONSE", "پاسخ نامعتبر از ملی‌پیامک.", isRetryable: true);
                }

                if (payload.RetStatus != 1)
                {
                    return Failure($"MELIPAYAMAK_{payload.RetStatus}", payload.StrRetStatus ?? "خطای نامشخص از ملی‌پیامک.", isRetryable: false);
                }

                return new SmsSendResult(
                    IsSuccess: true,
                    ProviderMessageId: payload.Value,
                    ProviderStatusCode: "1",
                    ErrorMessage: null,
                    IsRetryable: false,
                    Cost: null);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Failure("ERR_PROVIDER_TIMEOUT", "پاسخ ملی‌پیامک در زمان مجاز دریافت نشد.", isRetryable: true);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "MeliPayamak request failed (network)");
            return Failure("ERR_PROVIDER_UNREACHABLE", "ارتباط با ملی‌پیامک برقرار نشد.", isRetryable: true);
        }
    }

    public async Task<IReadOnlyList<SmsDeliveryStatus>> GetStatusAsync(
        SmsAccountConfig config, IReadOnlyList<string> providerMessageIds, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(config.Username) || providerMessageIds.Count == 0)
        {
            return [];
        }

        var client = httpClientFactory.CreateClient(SmsHttpClientNames.MeliPayamak);
        try
        {
            using var response = await client.PostAsJsonAsync("/api/SendSMS/GetDeliveries", new
            {
                username = config.Username,
                password = config.Password,
                recIds = providerMessageIds,
            }, ct);

            var payload = await response.Content.ReadFromJsonAsync<IReadOnlyList<MeliPayamakDeliveryEntry>>(ct);
            if (payload is null)
            {
                return [];
            }

            return payload
                .Select(e => new SmsDeliveryStatus(e.RecId, MapStatus(e.DeliveryState), e.DeliveryState.ToString()))
                .ToList();
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "MeliPayamak status query failed (network)");
            return [];
        }
    }

    public async Task<decimal?> GetCreditAsync(SmsAccountConfig config, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(config.Username))
        {
            return null;
        }

        var client = httpClientFactory.CreateClient(SmsHttpClientNames.MeliPayamak);
        try
        {
            using var response = await client.PostAsJsonAsync("/api/SendSMS/GetCredit", new
            {
                username = config.Username,
                password = config.Password,
            }, ct);

            var payload = await response.Content.ReadFromJsonAsync<MeliPayamakResponse>(ct);
            return payload is { RetStatus: 1 } && decimal.TryParse(payload.Value, out var credit) ? credit : null;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "MeliPayamak credit query failed (network)");
            return null;
        }
    }

    /// <summary>Best-effort mapping (see class doc) — unrecognized codes stay Sent rather than
    /// guessing at Delivered/Failed.</summary>
    private static SmsStatus MapStatus(int deliveryState) => deliveryState switch
    {
        1 => SmsStatus.Delivered,
        2 => SmsStatus.Failed,
        _ => SmsStatus.Sent,
    };

    private static SmsSendResult Failure(string code, string message, bool isRetryable) =>
        new(IsSuccess: false, ProviderMessageId: null, ProviderStatusCode: code, ErrorMessage: message, IsRetryable: isRetryable, Cost: null);

    private sealed record MeliPayamakResponse(string? Value, int RetStatus, string? StrRetStatus);

    private sealed record MeliPayamakDeliveryEntry(string RecId, int DeliveryState);
}
