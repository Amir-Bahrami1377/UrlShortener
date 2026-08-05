namespace Shortener.Api.Contracts;

public sealed record LinkStatusResponse(
    Guid RequestId,
    string? ClientRequestId,
    string Code,
    bool IsActive,
    DateTime ExpiresAt,
    string FileStatus,
    int OtpRequestCount,
    int DownloadCount,
    string SmsStatus);
