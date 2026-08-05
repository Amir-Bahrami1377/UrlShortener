namespace Shortener.Application.Services;

/// <summary>Magic-number validation (§M3.2.5) — never trust the file extension alone.</summary>
public static class FileTypeHelper
{
    public static bool IsValidSignature(ReadOnlySpan<byte> header, string extension) => extension switch
    {
        ".pdf" => header.Length >= 4
            && header[0] == 0x25 && header[1] == 0x50 && header[2] == 0x44 && header[3] == 0x46, // %PDF
        ".jpg" or ".jpeg" => header.Length >= 3
            && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF,
        ".png" => header.Length >= 8
            && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47
            && header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A,
        ".tif" or ".tiff" => header.Length >= 4
            && ((header[0] == 0x49 && header[1] == 0x49 && header[2] == 0x2A && header[3] == 0x00)   // little-endian "II*\0"
                || (header[0] == 0x4D && header[1] == 0x4D && header[2] == 0x00 && header[3] == 0x2A)), // big-endian "MM\0*"
        _ => false,
    };

    public static string GetContentType(string extension) => extension switch
    {
        ".pdf" => "application/pdf",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".tif" or ".tiff" => "image/tiff",
        _ => "application/octet-stream",
    };
}
