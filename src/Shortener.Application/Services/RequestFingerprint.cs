using System.Security.Cryptography;
using System.Text;

namespace Shortener.Application.Services;

/// <summary>One consistent way to hash IP/User-Agent for Redis keys and the dl:{token} binding,
/// so nothing ever compares a raw value against a hash by accident.</summary>
public static class RequestFingerprint
{
    public static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
