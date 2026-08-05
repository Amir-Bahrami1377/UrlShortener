using Shortener.Domain.Entities;

namespace Shortener.Application.Abstractions;

public interface IRetentionResolver
{
    /// <summary>
    /// Resolves the applicable retention policy in priority order: exact BatchTag match, then
    /// (ClientId, ReportId), then ClientId alone (ReportId IS NULL), then the global policy
    /// (ClientId IS NULL). Ties within a level are broken by the higher Priority value.
    /// The global policy is always seeded, so this should never fail to resolve in practice.
    /// </summary>
    Task<RetentionPolicy> ResolveAsync(int clientId, int reportId, string? batchTag, CancellationToken ct);
}
