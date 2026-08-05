using Microsoft.EntityFrameworkCore;
using Shortener.Application.Abstractions;
using Shortener.Domain.Entities;
using Shortener.Infrastructure.Persistence;

namespace Shortener.Infrastructure.Services;

public sealed class RetentionResolver(AppDbContext db) : IRetentionResolver
{
    public async Task<RetentionPolicy> ResolveAsync(int clientId, int reportId, string? batchTag, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(batchTag))
        {
            var byBatchTag = await BestMatch(db.RetentionPolicies.Where(p => p.BatchTag == batchTag), ct);
            if (byBatchTag is not null)
            {
                return byBatchTag;
            }
        }

        var byClientAndReport = await BestMatch(
            db.RetentionPolicies.Where(p => p.ClientId == clientId && p.ReportId == reportId), ct);
        if (byClientAndReport is not null)
        {
            return byClientAndReport;
        }

        var byClientOnly = await BestMatch(
            db.RetentionPolicies.Where(p => p.ClientId == clientId && p.ReportId == null), ct);
        if (byClientOnly is not null)
        {
            return byClientOnly;
        }

        var global = await BestMatch(db.RetentionPolicies.Where(p => p.ClientId == null), ct);

        return global ?? throw new InvalidOperationException(
            "No retention policy could be resolved, including the global fallback. " +
            "The global policy (ClientId = NULL) must always exist — check that the seeder ran.");
    }

    private static Task<RetentionPolicy?> BestMatch(IQueryable<RetentionPolicy> query, CancellationToken ct) =>
        query.Where(p => p.IsActive).OrderByDescending(p => p.Priority).FirstOrDefaultAsync(ct);
}
