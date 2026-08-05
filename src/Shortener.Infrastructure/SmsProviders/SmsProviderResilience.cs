using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace Shortener.Infrastructure.SmsProviders;

/// <summary>§M5.1 — "Retry three times, only for network errors and 5xx; a 4xx is a semantic error
/// (IsRetryable = false)." This is transport-level retry only; each provider still separately
/// inspects its parsed response body to decide IsRetryable for application-level failures (e.g. a
/// Kavenegar response that is HTTP 200 but carries an authentication or credit error internally).</summary>
internal static class SmsProviderResilience
{
    public static void AddSmsRetryPolicy(this IHttpClientBuilder builder) =>
        builder.AddResilienceHandler("sms-transport-retry", static pipeline =>
        {
            pipeline.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                BackoffType = DelayBackoffType.Exponential,
                Delay = TimeSpan.FromMilliseconds(500),
                ShouldHandle = static args => ValueTask.FromResult(
                    args.Outcome.Exception is HttpRequestException or TaskCanceledException ||
                    args.Outcome.Result is { StatusCode: var code } && (int)code >= 500),
            });
        });

    /// <summary>§M5.1's per-message-type timeout (10s OTP / 30s bulk) can't be a fixed HttpClient.Timeout
    /// since the same provider serves both purposes across different SmsAccounts — applied per-call instead.</summary>
    public static TimeSpan TimeoutFor(Domain.Enums.SmsMessageType messageType) =>
        messageType == Domain.Enums.SmsMessageType.Otp ? TimeSpan.FromSeconds(10) : TimeSpan.FromSeconds(30);
}
