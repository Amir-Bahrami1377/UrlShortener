namespace Shortener.Application.Common;

/// <summary>Error codes from IMPLEMENTATION_PLAN.md Appendix B, with their fixed HTTP status.</summary>
public static class ErrorCodes
{
    public const string MissingApiKey = "ERR_MISSING_API_KEY";
    public const string InvalidApiKey = "ERR_INVALID_API_KEY";
    public const string ClientInactive = "ERR_CLIENT_INACTIVE";
    public const string MetadataFirst = "ERR_METADATA_FIRST";
    public const string Validation = "ERR_VALIDATION";
    public const string FileTooLarge = "ERR_FILE_TOO_LARGE";
    public const string InvalidFileType = "ERR_INVALID_FILE_TYPE";
    public const string DiskFull = "ERR_DISK_FULL";
    public const string CodeGenerationFailed = "ERR_CODE_GENERATION_FAILED";
    public const string RateLimited = "ERR_RATE_LIMITED";
    public const string LinkNotFound = "ERR_LINK_NOT_FOUND";
    public const string LinkInactive = "ERR_LINK_INACTIVE";
    public const string LinkExpired = "ERR_LINK_EXPIRED";
    public const string FileDeleted = "ERR_FILE_DELETED";
    public const string OtpCooldown = "ERR_OTP_COOLDOWN";
    public const string OtpLimitLink = "ERR_OTP_LIMIT_LINK";
    public const string OtpLimitIp = "ERR_OTP_LIMIT_IP";
    public const string OtpNotConfigured = "ERR_OTP_NOT_CONFIGURED";
    public const string OtpExpired = "ERR_OTP_EXPIRED";
    public const string OtpInvalid = "ERR_OTP_INVALID";
    public const string TooManyAttempts = "ERR_TOO_MANY_ATTEMPTS";
    public const string TokenUsedOrExpired = "ERR_TOKEN_USED_OR_EXPIRED";
    public const string TokenMismatch = "ERR_TOKEN_MISMATCH";
    public const string FileMissing = "ERR_FILE_MISSING";

    /// <summary>Not part of Appendix B's business error codes — a generic catch-all for unexpected bugs.</summary>
    public const string Internal = "ERR_INTERNAL";

    public static readonly IReadOnlyDictionary<string, int> HttpStatus = new Dictionary<string, int>
    {
        [Internal] = StatusCodes.Status500InternalServerError,
        [MissingApiKey] = StatusCodes.Status401Unauthorized,
        [InvalidApiKey] = StatusCodes.Status401Unauthorized,
        [ClientInactive] = StatusCodes.Status403Forbidden,
        [MetadataFirst] = StatusCodes.Status400BadRequest,
        [Validation] = StatusCodes.Status400BadRequest,
        [FileTooLarge] = StatusCodes.Status413PayloadTooLarge,
        [InvalidFileType] = StatusCodes.Status415UnsupportedMediaType,
        [DiskFull] = StatusCodes.Status507InsufficientStorage,
        [CodeGenerationFailed] = StatusCodes.Status500InternalServerError,
        [RateLimited] = StatusCodes.Status429TooManyRequests,
        [LinkNotFound] = StatusCodes.Status404NotFound,
        [LinkInactive] = StatusCodes.Status410Gone,
        [LinkExpired] = StatusCodes.Status410Gone,
        [FileDeleted] = StatusCodes.Status410Gone,
        [OtpCooldown] = StatusCodes.Status429TooManyRequests,
        [OtpLimitLink] = StatusCodes.Status429TooManyRequests,
        [OtpLimitIp] = StatusCodes.Status429TooManyRequests,
        [OtpNotConfigured] = StatusCodes.Status500InternalServerError,
        [OtpExpired] = StatusCodes.Status400BadRequest,
        [OtpInvalid] = StatusCodes.Status400BadRequest,
        [TooManyAttempts] = StatusCodes.Status429TooManyRequests,
        [TokenUsedOrExpired] = StatusCodes.Status410Gone,
        [TokenMismatch] = StatusCodes.Status403Forbidden,
        [FileMissing] = StatusCodes.Status410Gone,
    };

    /// <summary>Persian summaries taken verbatim from IMPLEMENTATION_PLAN.md Appendix B's "معنا" column.</summary>
    public static readonly IReadOnlyDictionary<string, string> DefaultTitle = new Dictionary<string, string>
    {
        [Internal] = "خطای غیرمنتظره سرور",
        [MissingApiKey] = "هدر X-Api-Key ارسال نشده",
        [InvalidApiKey] = "کلید نامعتبر، منقضی یا باطل‌شده",
        [ClientInactive] = "کلاینت غیرفعال است",
        [MetadataFirst] = "بخش metadata قبل از file نیامده",
        [Validation] = "خطای اعتبارسنجی فیلدها",
        [FileTooLarge] = "حجم فایل بیش از حد مجاز است",
        [InvalidFileType] = "Magic Number با فرمت مجاز مطابقت ندارد",
        [DiskFull] = "فضای دیسک بیش از آستانه",
        [CodeGenerationFailed] = "تولید کد یکتا پس از چند تلاش ناموفق ماند",
        [RateLimited] = "سقف نرخ درخواست",
        [LinkNotFound] = "کد لینک وجود ندارد",
        [LinkInactive] = "لینک غیرفعال شده",
        [LinkExpired] = "مهلت دانلود تمام شده",
        [FileDeleted] = "فایل به دلیل اتمام نگهداشت حذف شده است",
        [OtpCooldown] = "هنوز زمان ارسال مجدد نرسیده",
        [OtpLimitLink] = "سقف درخواست OTP برای این لینک",
        [OtpLimitIp] = "سقف درخواست OTP برای این IP",
        [OtpNotConfigured] = "حساب یا قالب OTP برای کلاینت تعریف نشده",
        [OtpExpired] = "کد منقضی شده",
        [OtpInvalid] = "کد نادرست",
        [TooManyAttempts] = "قفل موقت پس از ۵ تلاش نادرست",
        [TokenUsedOrExpired] = "توکن دانلود مصرف یا منقضی شده",
        [TokenMismatch] = "IP یا User-Agent با زمان صدور توکن نمی‌خواند",
        [FileMissing] = "فایل روی دیسک یافت نشد (ناسازگاری)",
    };
}

/// <summary>Minimal stand-in for Microsoft.AspNetCore.Http.StatusCodes so Application has no ASP.NET Core dependency.</summary>
internal static class StatusCodes
{
    public const int Status400BadRequest = 400;
    public const int Status401Unauthorized = 401;
    public const int Status403Forbidden = 403;
    public const int Status404NotFound = 404;
    public const int Status410Gone = 410;
    public const int Status413PayloadTooLarge = 413;
    public const int Status415UnsupportedMediaType = 415;
    public const int Status429TooManyRequests = 429;
    public const int Status500InternalServerError = 500;
    public const int Status507InsufficientStorage = 507;
}
