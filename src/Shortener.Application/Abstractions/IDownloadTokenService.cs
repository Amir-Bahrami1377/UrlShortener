using Shortener.Application.Contracts;

namespace Shortener.Application.Abstractions;

public interface IDownloadTokenService
{
    /// <summary>One-shot consume via GETDEL — returns null if the token was already used, expired, or never existed.</summary>
    Task<DownloadTokenPayload?> ConsumeAsync(string token, CancellationToken ct);
}
