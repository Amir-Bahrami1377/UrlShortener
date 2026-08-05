namespace Shortener.Api.Contracts;

public sealed record UploadLinkResponse(
    Guid RequestId,
    string? ClientRequestId,
    string Code,
    string ShortUrl,
    DateTime FileExpiresAt,
    string SmsStatus,
    bool IsDuplicate);
