namespace Shortener.Domain.Entities;

/// <summary>Catalog of supported SMS gateway integrations (e.g. kavenegar, melipayamak, farazsms, fake).</summary>
public class SmsProvider
{
    public int Id { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public bool SupportsPattern { get; set; }
    public bool SupportsDlr { get; set; }
    public bool IsActive { get; set; } = true;
}
