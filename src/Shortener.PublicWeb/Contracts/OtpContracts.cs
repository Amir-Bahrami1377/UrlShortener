namespace Shortener.PublicWeb.Contracts;

public sealed record VerifyOtpRequest(string Otp);

public sealed record RequestOtpResponse(int CooldownSeconds, int TtlSeconds);

public sealed record VerifyOtpResponse(string DownloadUrl);
