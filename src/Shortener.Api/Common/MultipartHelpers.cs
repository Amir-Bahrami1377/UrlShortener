using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;

namespace Shortener.Api.Common;

public static class MultipartHelpers
{
    // Despite IMPLEMENTATION_PLAN.md's snippet calling `MultipartRequestHelper.GetBoundary(...)` as if
    // it were a framework API, that class doesn't exist in ASP.NET Core — it's sample code from
    // Microsoft's file-upload tutorial that projects are expected to copy in. This inlines the same
    // extraction (unquote the boundary parameter, enforce a sane length limit) without the fake dependency.
    private const int BoundaryLengthLimit = 128;

    public static bool TryGetBoundary(HttpRequest request, out string boundary)
    {
        boundary = string.Empty;
        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var mediaType) ||
            !mediaType.MediaType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var value = HeaderUtilities.RemoveQuotes(mediaType.Boundary).Value;
        if (string.IsNullOrWhiteSpace(value) || value.Length > BoundaryLengthLimit)
        {
            return false;
        }

        boundary = value;
        return true;
    }

    public static bool IsSectionNamed(MultipartSection section, string name) =>
        ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var cd) &&
        string.Equals(cd.Name.Value, name, StringComparison.Ordinal);

    public static string? GetFileName(MultipartSection section)
    {
        if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var cd))
        {
            return null;
        }

        var name = cd.FileNameStar.HasValue ? cd.FileNameStar.Value : cd.FileName.Value;
        return string.IsNullOrEmpty(name) ? null : name;
    }

    public static async Task<int> ReadFullyAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(totalRead), ct);
            if (read == 0)
            {
                break;
            }

            totalRead += read;
        }

        return totalRead;
    }
}
