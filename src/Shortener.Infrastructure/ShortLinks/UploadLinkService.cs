using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Shortener.Application.Abstractions;
using Shortener.Application.Contracts;
using Shortener.Application.Services;
using Shortener.Domain.Entities;
using Shortener.Domain.Enums;
using Shortener.Infrastructure.Observability;
using Shortener.Infrastructure.Persistence;

namespace Shortener.Infrastructure.ShortLinks;

public sealed class UploadLinkService(
    AppDbContext db,
    IFileStorage fileStorage,
    IRetentionResolver retentionResolver,
    ShortCodeGenerator codeGenerator,
    ILinkCache linkCache,
    ILinkSmsStatusResolver smsStatusResolver,
    ShortenerMetrics metrics) : IUploadLinkService
{
    public async Task<LinkOperationResult?> FindExistingAsync(int clientId, UploadLinkMetadata metadata, CancellationToken ct)
    {
        var existing = await db.ShortLinks
            .Where(l => l.ClientId == clientId && l.Shop == metadata.Shop && l.Shod == metadata.Shod &&
                        l.Radif == metadata.Radif && l.ReportId == metadata.ReportId && l.IsActive)
            .Select(l => new { l.Id, l.RequestId, l.ClientRequestId, l.Code, l.ExpiresAt })
            .FirstOrDefaultAsync(ct);

        if (existing is null)
        {
            return null;
        }

        var smsStatus = await smsStatusResolver.GetStatusAsync(existing.Id, ct);

        return new LinkOperationResult(
            existing.RequestId, existing.ClientRequestId, existing.Code, existing.ExpiresAt,
            smsStatus.ToString(), IsDuplicate: true);
    }

    public async Task<LinkOperationResult> CreateAsync(
        int clientId, int? apiKeyId, Guid requestId, UploadLinkMetadata metadata,
        Stream fileStream, string extension, string originalFileName, CancellationToken ct)
    {
        var savedFile = await fileStorage.SaveAsync(fileStream, extension, ct);

        try
        {
            return await InsertAsync(clientId, apiKeyId, requestId, metadata, extension, originalFileName, savedFile, ct);
        }
        catch (DbUpdateException ex) when (IsBusinessKeyViolation(ex))
        {
            await fileStorage.DeleteAsync(savedFile.StorageKey, ct);
            return await FindExistingAsync(clientId, metadata, ct)
                   ?? throw new InvalidOperationException(
                       "Unique-index violation on the business key but no matching row was found afterwards.", ex);
        }
        catch
        {
            await fileStorage.DeleteAsync(savedFile.StorageKey, ct);
            throw;
        }
    }

    private async Task<LinkOperationResult> InsertAsync(
        int clientId, int? apiKeyId, Guid requestId, UploadLinkMetadata metadata,
        string extension, string originalFileName, StoredFileResult savedFile, CancellationToken ct)
    {
        var retentionPolicy = await retentionResolver.ResolveAsync(clientId, metadata.ReportId, metadata.BatchTag, ct);
        var storedAt = DateTime.UtcNow;
        var fileExpiresAt = storedAt.AddDays(retentionPolicy.RetentionDays);
        var contentType = FileTypeHelper.GetContentType(extension);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var storedFile = new StoredFile
        {
            FileGuid = savedFile.FileGuid,
            ClientId = clientId,
            RetentionPolicyId = retentionPolicy.Id,
            StorageKey = savedFile.StorageKey,
            OriginalFileName = originalFileName,
            ContentType = contentType,
            Extension = extension,
            SizeBytes = savedFile.SizeBytes,
            Sha256 = savedFile.Sha256,
            Status = FileStatus.Active,
            StoredAt = storedAt,
            FileExpiresAt = fileExpiresAt,
        };
        db.StoredFiles.Add(storedFile);
        await db.SaveChangesAsync(ct);

        var code = await codeGenerator.GenerateAsync(ct);

        var shortLink = new ShortLink
        {
            Code = code,
            RequestId = requestId,
            ClientRequestId = metadata.ClientRequestId,
            StoredFileId = storedFile.Id,
            ClientId = clientId,
            ApiKeyId = apiKeyId,
            Shop = metadata.Shop,
            Shod = metadata.Shod,
            Radif = metadata.Radif,
            ReportId = metadata.ReportId,
            ReportName = metadata.ReportName,
            PhoneNumber = metadata.PhoneNumber,
            BatchTag = metadata.BatchTag,
            ExpiresAt = fileExpiresAt,
            IsActive = true,
        };
        db.ShortLinks.Add(shortLink);
        await db.SaveChangesAsync(ct);

        if (metadata.SendSmsImmediately)
        {
            db.OutboxMessages.Add(new OutboxMessage
            {
                Id = Guid.CreateVersion7(),
                Type = OutboxMessageTypes.LinkSms,
                PayloadJson = JsonSerializer.Serialize(new LinkSmsPayload(shortLink.Id)),
                Status = OutboxStatus.Pending,
            });
            await db.SaveChangesAsync(ct);
        }

        await transaction.CommitAsync(ct);

        metrics.LinkCreated(clientId);
        metrics.FileUploadBytes(clientId, savedFile.SizeBytes);

        await linkCache.SetAsync(code, new LinkCacheEntry(
            shortLink.Id, storedFile.Id, metadata.ReportName, metadata.PhoneNumber, fileExpiresAt,
            IsActive: true, FileStatus.Active, savedFile.StorageKey, contentType, extension, clientId), ct);

        var smsStatus = await smsStatusResolver.GetStatusAsync(shortLink.Id, ct);
        return new LinkOperationResult(requestId, metadata.ClientRequestId, code, fileExpiresAt, smsStatus.ToString(), IsDuplicate: false);
    }

    private static bool IsBusinessKeyViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 } sqlEx &&
        sqlEx.Message.Contains("UX_ShortLinks_Business", StringComparison.Ordinal);
}
