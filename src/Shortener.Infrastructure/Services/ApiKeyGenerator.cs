using System.Security.Cryptography;
using System.Text;
using Shortener.Application.Abstractions;

namespace Shortener.Infrastructure.Services;

public sealed class ApiKeyGenerator : IApiKeyGenerator
{
    public GeneratedApiKey Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var rawKey = "sk_" + Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawKey))).ToLowerInvariant();
        return new GeneratedApiKey(rawKey, hash, rawKey[..12]);
    }
}
