namespace Shortener.PublicWeb.Common;

public static class RateLimitPolicies
{
    /// <summary>A coarse per-IP backstop (60/min), on top of the per-link/per-IP hourly Redis
    /// counters already enforced inside IOtpService (§M4.3 steps 3-4). Applied to the OTP action
    /// endpoints and, per §M8.5, to GET /s/{code} itself via [EnableRateLimiting] on ShortLinkModel.</summary>
    public const string PublicLink = "PublicLink";
}
