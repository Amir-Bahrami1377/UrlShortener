namespace Shortener.Application.Contracts;

/// <summary>dl:{token} Redis value (§M4.4.5) — consumed exactly once via GETDEL (§M4.5.1).</summary>
public sealed record DownloadTokenPayload(long ShortLinkId, string IpHash, string UserAgentHash);
