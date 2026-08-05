namespace Shortener.Application.Contracts;

/// <summary>OutboxMessage.PayloadJson shape for Type="LinkSms" — the download-link SMS for one ShortLink.</summary>
public sealed record LinkSmsPayload(long ShortLinkId);
