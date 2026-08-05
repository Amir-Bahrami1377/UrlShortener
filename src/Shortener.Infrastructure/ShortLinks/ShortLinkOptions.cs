namespace Shortener.Infrastructure.ShortLinks;

public sealed class ShortLinkOptions
{
    public required string BaseUrl { get; set; }
    public int CodeLength { get; set; } = 6;
    public int MaxGenerationRetries { get; set; } = 5;
    public int CacheTtlMinutes { get; set; } = 60;
}
