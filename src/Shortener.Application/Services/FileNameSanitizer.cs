namespace Shortener.Application.Services;

/// <summary>§M4.5.6 — strips filesystem-hostile/control characters, keeps everything else
/// (including Persian) so the downloaded file's name stays readable.</summary>
public static class FileNameSanitizer
{
    private static readonly char[] ForbiddenChars = ['\\', '/', ':', '*', '?', '"', '<', '>', '|'];
    private const int MaxLength = 150;

    public static string Sanitize(string fileName)
    {
        var chars = fileName.Where(c => !ForbiddenChars.Contains(c) && !char.IsControl(c)).ToArray();
        var sanitized = new string(chars).Trim();
        if (sanitized.Length == 0)
        {
            sanitized = "file";
        }

        return sanitized.Length > MaxLength ? sanitized[..MaxLength] : sanitized;
    }
}
