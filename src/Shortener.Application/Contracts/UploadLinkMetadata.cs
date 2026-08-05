namespace Shortener.Application.Contracts;

/// <summary>The "metadata" multipart section of POST /api/v1/links (§M3.2).</summary>
public sealed record UploadLinkMetadata(
    string Shop,
    string Shod,
    string Radif,
    int ReportId,
    string ReportName,
    string PhoneNumber,
    string? ClientRequestId,
    string? BatchTag,
    bool SendSmsImmediately = true);
