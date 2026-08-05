namespace Shortener.Application.Abstractions;

/// <summary>§M2.3's key generation algorithm: 32 random bytes → Base64Url with an "sk_" prefix.</summary>
public interface IApiKeyGenerator
{
    GeneratedApiKey Generate();
}

/// <summary>RawKey must be shown to the operator exactly once and never persisted — only KeyHash/KeyPrefix are stored.</summary>
public sealed record GeneratedApiKey(string RawKey, string KeyHash, string KeyPrefix);
