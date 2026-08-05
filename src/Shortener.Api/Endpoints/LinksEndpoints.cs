using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shortener.Api.Authentication;
using Shortener.Api.Common;
using Shortener.Api.Contracts;
using Shortener.Application.Abstractions;
using Shortener.Application.Common;
using Shortener.Application.Contracts;
using Shortener.Application.Services;
using Shortener.Domain.Entities;
using Shortener.Domain.Enums;
using Shortener.Domain.Exceptions;
using Shortener.Infrastructure.FileStorage;
using Shortener.Infrastructure.Persistence;
using Shortener.Infrastructure.ShortLinks;

namespace Shortener.Api.Endpoints;

public static class LinksEndpoints
{
    private static readonly JsonSerializerOptions MetadataJsonOptions = new(JsonSerializerDefaults.Web);

    public static void MapLinksEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/links")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitPolicies.PerClient);

        group.MapPost("", UploadLinkAsync);
        group.MapPost("/dispatch-sms", DispatchBatchAsync);
        group.MapPost("/{requestId:guid}/dispatch-sms", DispatchSingleAsync);
        group.MapGet("/{requestId:guid}", GetByRequestIdAsync);
        group.MapGet("/by-client-request/{clientRequestId}", GetByClientRequestIdAsync);
    }

    private static async Task<IResult> UploadLinkAsync(
        HttpContext httpContext,
        IUploadLinkService uploadLinkService,
        IFileStorage fileStorage,
        IOptions<FileStorageOptions> fileStorageOptions,
        IOptions<ShortLinkOptions> shortLinkOptions,
        IValidator<UploadLinkMetadata> validator,
        CancellationToken ct)
    {
        var requestId = Guid.CreateVersion7();
        httpContext.Response.Headers["X-Request-Id"] = requestId.ToString();
        httpContext.Items["RequestId"] = requestId.ToString();

        var clientId = httpContext.User.GetClientId();
        var apiKeyId = httpContext.User.GetApiKeyId();

        // Minimal API endpoints don't honor the [RequestSizeLimit] MVC attribute the plan document
        // assumes — Kestrel's ~28.6MB default applies unless overridden here explicitly.
        var maxRequestBodySizeFeature = httpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (maxRequestBodySizeFeature is { IsReadOnly: false })
        {
            maxRequestBodySizeFeature.MaxRequestBodySize = 4_194_304;
        }

        var request = httpContext.Request;
        if (!MultipartHelpers.TryGetBoundary(request, out var boundary))
        {
            throw new AppException(ErrorCodes.MetadataFirst, "درخواست باید از نوع multipart/form-data باشد.");
        }

        var reader = new MultipartReader(boundary, request.Body);

        var metadataSection = await reader.ReadNextSectionAsync(ct);
        if (metadataSection is null || !MultipartHelpers.IsSectionNamed(metadataSection, "metadata"))
        {
            throw new AppException(ErrorCodes.MetadataFirst, "بخش metadata باید اولین Part در درخواست باشد.");
        }

        UploadLinkMetadata metadata;
        try
        {
            metadata = await JsonSerializer.DeserializeAsync<UploadLinkMetadata>(metadataSection.Body, MetadataJsonOptions, ct)
                       ?? throw new JsonException("metadata body was empty");
        }
        catch (JsonException)
        {
            throw new AppException(ErrorCodes.Validation, "بخش metadata معتبر نیست.");
        }

        var validationResult = await validator.ValidateAsync(metadata, ct);
        if (!validationResult.IsValid)
        {
            throw new AppException(ErrorCodes.Validation, string.Join(" ", validationResult.Errors.Select(e => e.ErrorMessage)));
        }

        var existing = await uploadLinkService.FindExistingAsync(clientId, metadata, ct);
        if (existing is not null)
        {
            return Results.Ok(ToResponse(existing, shortLinkOptions.Value.BaseUrl));
        }

        var diskSpace = fileStorage.GetDiskSpace();
        if (diskSpace.UsedPercent > fileStorageOptions.Value.DiskRejectThresholdPercent)
        {
            throw new AppException(ErrorCodes.DiskFull, "فضای ذخیره‌سازی سرور پر است.");
        }

        var fileSection = await reader.ReadNextSectionAsync(ct);
        if (fileSection is null || !MultipartHelpers.IsSectionNamed(fileSection, "file"))
        {
            throw new AppException(ErrorCodes.Validation, "بخش file یافت نشد.");
        }

        var originalFileName = MultipartHelpers.GetFileName(fileSection) ?? "upload";
        var extension = Path.GetExtension(originalFileName).ToLowerInvariant();
        if (string.IsNullOrEmpty(extension) || !fileStorageOptions.Value.AllowedExtensions.Contains(extension))
        {
            throw new AppException(ErrorCodes.InvalidFileType, "پسوند فایل مجاز نیست.");
        }

        var headerBuffer = new byte[12];
        var headerLength = await MultipartHelpers.ReadFullyAsync(fileSection.Body, headerBuffer, ct);
        if (!FileTypeHelper.IsValidSignature(headerBuffer.AsSpan(0, headerLength), extension))
        {
            throw new AppException(ErrorCodes.InvalidFileType, "محتوای فایل با فرمت اعلام‌شده مطابقت ندارد.");
        }

        await using var prefixedStream = new PrefixedStream(headerBuffer.AsMemory(0, headerLength), fileSection.Body);

        var created = await uploadLinkService.CreateAsync(
            clientId, apiKeyId, requestId, metadata, prefixedStream, extension, originalFileName, ct);

        return Results.Created($"/api/v1/links/{created.RequestId}", ToResponse(created, shortLinkOptions.Value.BaseUrl));
    }

    private static UploadLinkResponse ToResponse(LinkOperationResult result, string baseUrl) => new(
        result.RequestId,
        result.ClientRequestId,
        result.Code,
        $"{baseUrl.TrimEnd('/')}/s/{result.Code}",
        result.FileExpiresAt,
        result.SmsStatus,
        result.IsDuplicate);

    // §M3.4 — for the peak-day "upload now, send later" scenario. Idempotent: a link that already
    // has an in-flight LinkSms OutboxMessage or an SmsMessage is skipped rather than dispatched twice.
    private static async Task<IResult> DispatchBatchAsync(
        DispatchSmsRequest body, AppDbContext db, ILinkSmsStatusResolver smsStatusResolver, HttpContext httpContext, CancellationToken ct)
    {
        var clientId = httpContext.User.GetClientId();
        var candidateIds = await db.ShortLinks
            .Where(l => l.ClientId == clientId && l.IsActive && l.BatchTag == body.BatchTag)
            .Select(l => l.Id)
            .ToListAsync(ct);

        var (queued, skipped) = await DispatchAsync(db, smsStatusResolver, candidateIds, ct);
        return Results.Ok(new DispatchSmsResponse(queued, skipped));
    }

    private static async Task<IResult> DispatchSingleAsync(
        Guid requestId, AppDbContext db, ILinkSmsStatusResolver smsStatusResolver, HttpContext httpContext, CancellationToken ct)
    {
        var clientId = httpContext.User.GetClientId();
        var link = await db.ShortLinks.FirstOrDefaultAsync(l => l.ClientId == clientId && l.RequestId == requestId, ct);
        if (link is null)
        {
            throw new AppException(ErrorCodes.LinkNotFound, "لینکی با این RequestId یافت نشد.");
        }

        var (queued, skipped) = await DispatchAsync(db, smsStatusResolver, [link.Id], ct);
        return Results.Ok(new DispatchSmsResponse(queued, skipped));
    }

    private static async Task<(int Queued, int Skipped)> DispatchAsync(
        AppDbContext db, ILinkSmsStatusResolver smsStatusResolver, List<long> candidateLinkIds, CancellationToken ct)
    {
        if (candidateLinkIds.Count == 0)
        {
            return (0, 0);
        }

        var alreadyDispatched = await smsStatusResolver.GetDispatchedLinkIdsAsync(ct);
        var toDispatch = candidateLinkIds.Where(id => !alreadyDispatched.Contains(id)).ToList();

        foreach (var linkId in toDispatch)
        {
            db.OutboxMessages.Add(new OutboxMessage
            {
                Id = Guid.CreateVersion7(),
                Type = OutboxMessageTypes.LinkSms,
                PayloadJson = JsonSerializer.Serialize(new LinkSmsPayload(linkId)),
                Status = OutboxStatus.Pending,
            });
        }

        await db.SaveChangesAsync(ct);
        return (toDispatch.Count, candidateLinkIds.Count - toDispatch.Count);
    }

    private static async Task<IResult> GetByRequestIdAsync(
        Guid requestId, AppDbContext db, ILinkSmsStatusResolver smsStatusResolver, HttpContext httpContext, CancellationToken ct)
    {
        var clientId = httpContext.User.GetClientId();
        var link = await db.ShortLinks.Include(l => l.StoredFile)
            .FirstOrDefaultAsync(l => l.ClientId == clientId && l.RequestId == requestId, ct);

        if (link is null)
        {
            throw new AppException(ErrorCodes.LinkNotFound, "لینکی با این RequestId یافت نشد.");
        }

        return Results.Ok(await BuildStatusResponseAsync(link, smsStatusResolver, ct));
    }

    private static async Task<IResult> GetByClientRequestIdAsync(
        string clientRequestId, AppDbContext db, ILinkSmsStatusResolver smsStatusResolver, HttpContext httpContext, CancellationToken ct)
    {
        var clientId = httpContext.User.GetClientId();
        var link = await db.ShortLinks.Include(l => l.StoredFile)
            .Where(l => l.ClientId == clientId && l.ClientRequestId == clientRequestId)
            .OrderByDescending(l => l.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (link is null)
        {
            throw new AppException(ErrorCodes.LinkNotFound, "لینکی با این ClientRequestId یافت نشد.");
        }

        return Results.Ok(await BuildStatusResponseAsync(link, smsStatusResolver, ct));
    }

    private static async Task<LinkStatusResponse> BuildStatusResponseAsync(
        ShortLink link, ILinkSmsStatusResolver smsStatusResolver, CancellationToken ct)
    {
        var smsStatus = await smsStatusResolver.GetStatusAsync(link.Id, ct);

        return new LinkStatusResponse(
            link.RequestId, link.ClientRequestId, link.Code, link.IsActive, link.ExpiresAt,
            link.StoredFile!.Status.ToString(), link.OtpRequestCount, link.DownloadCount,
            smsStatus.ToString());
    }
}
