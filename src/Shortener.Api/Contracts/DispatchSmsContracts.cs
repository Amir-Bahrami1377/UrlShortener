namespace Shortener.Api.Contracts;

public sealed record DispatchSmsRequest(string BatchTag, int? RatePerMinute = null);

public sealed record DispatchSmsResponse(int QueuedCount, int SkippedCount);
