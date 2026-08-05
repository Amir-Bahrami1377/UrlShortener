using Microsoft.EntityFrameworkCore;
using Shortener.Application.Abstractions;
using Shortener.Infrastructure.Persistence;

namespace Shortener.Infrastructure.ShortLinks;

public sealed class ShortLinkStateResolver(AppDbContext db, ILinkCache linkCache) : IShortLinkStateResolver
{
    public async Task<ShortLinkState?> ResolveAsync(string code, CancellationToken ct)
    {
        var cached = await linkCache.GetAsync(code, ct);
        if (cached is not null)
        {
            return new ShortLinkState(
                cached.ShortLinkId, cached.ClientId, code, cached.ReportName, cached.PhoneNumber,
                cached.IsActive, cached.ExpiresAt, cached.FileStatus, cached.StorageKey, cached.ContentType, cached.Extension);
        }

        // Cache miss (true miss or Redis down) — read straight from SQL, the source of truth.
        var link = await db.ShortLinks
            .Include(l => l.StoredFile)
            .Where(l => l.Code == code)
            .Select(l => new
            {
                l.Id,
                l.StoredFileId,
                l.ClientId,
                l.ReportName,
                l.PhoneNumber,
                l.IsActive,
                l.ExpiresAt,
                FileStatus = l.StoredFile!.Status,
                l.StoredFile!.StorageKey,
                l.StoredFile.ContentType,
                l.StoredFile.Extension,
            })
            .FirstOrDefaultAsync(ct);

        if (link is null)
        {
            return null;
        }

        var state = new ShortLinkState(
            link.Id, link.ClientId, code, link.ReportName, link.PhoneNumber,
            link.IsActive, link.ExpiresAt, link.FileStatus, link.StorageKey, link.ContentType, link.Extension);

        await linkCache.SetAsync(code, new LinkCacheEntry(
            state.ShortLinkId, link.StoredFileId, state.ReportName, state.PhoneNumber, state.ExpiresAt, state.IsActive,
            state.FileStatus, state.StorageKey, state.ContentType, state.Extension, state.ClientId), ct);

        return state;
    }
}
