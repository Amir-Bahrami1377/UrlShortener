namespace Shortener.Domain.Enums;

public enum LinkAccessType : byte
{
    View = 0,
    OtpRequested = 1,
    VerifySuccess = 2,
    VerifyFailed = 3,
    Locked = 4,
    Download = 5,
    DownloadFailed = 6
}
