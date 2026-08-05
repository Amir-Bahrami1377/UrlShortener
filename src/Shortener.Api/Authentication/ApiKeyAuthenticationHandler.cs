using System.Globalization;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Shortener.Application.Abstractions;
using Shortener.Application.Common;
using Shortener.Infrastructure.Web;

namespace Shortener.Api.Authentication;

public static class ApiKeyAuthenticationDefaults
{
    public const string SchemeName = "ApiKey";
    public const string HeaderName = "X-Api-Key";
}

/// <summary>
/// Implements §M3.1: read X-Api-Key, validate via IApiKeyLookupService (Redis-cached, SQL fallback),
/// and expose ClientId/ApiKeyId as claims. Writes the RFC 7807 body itself on failure since ASP.NET
/// Core's default challenge/forbid handlers don't know about our error-code shape.
/// </summary>
public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IApiKeyLookupService lookupService)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    private const string ErrorCodeItemKey = "ApiKeyAuthErrorCode";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ApiKeyAuthenticationDefaults.HeaderName, out var headerValues) ||
            string.IsNullOrWhiteSpace(headerValues.FirstOrDefault()))
        {
            Context.Items[ErrorCodeItemKey] = ErrorCodes.MissingApiKey;
            return AuthenticateResult.NoResult();
        }

        var result = await lookupService.ValidateAsync(headerValues.First()!, Context.RequestAborted);

        if (result.Status == ApiKeyValidationStatus.Valid)
        {
            var claims = new[]
            {
                new Claim("client_id", result.ClientId!.Value.ToString(CultureInfo.InvariantCulture)),
                new Claim("api_key_id", result.ApiKeyId!.Value.ToString(CultureInfo.InvariantCulture)),
            };
            var identity = new ClaimsIdentity(claims, ApiKeyAuthenticationDefaults.SchemeName);
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), ApiKeyAuthenticationDefaults.SchemeName);
            return AuthenticateResult.Success(ticket);
        }

        Context.Items[ErrorCodeItemKey] = result.Status == ApiKeyValidationStatus.ClientInactive
            ? ErrorCodes.ClientInactive
            : ErrorCodes.InvalidApiKey;
        return AuthenticateResult.NoResult();
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var errorCode = Context.Items.TryGetValue(ErrorCodeItemKey, out var code) && code is string s
            ? s
            : ErrorCodes.InvalidApiKey;
        return ProblemResponseWriter.WriteAsync(Context, errorCode);
    }
}

public static class ClaimsPrincipalExtensions
{
    public static int GetClientId(this ClaimsPrincipal principal) =>
        int.Parse(principal.FindFirstValue("client_id")!, CultureInfo.InvariantCulture);

    public static int? GetApiKeyId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue("api_key_id");
        return value is null ? null : int.Parse(value, CultureInfo.InvariantCulture);
    }
}
