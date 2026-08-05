using FluentAssertions;
using Shortener.Application.Services;

namespace Shortener.UnitTests.Services;

/// <summary>§M3.2.5 — magic-number validation is the actual security boundary (extensions are
/// attacker-controlled), so every supported signature and the "wrong extension entirely" case both
/// need direct coverage, not just incidental exercise via a single PDF upload in an integration test.</summary>
public sealed class FileTypeHelperTests
{
    [Theory]
    [InlineData(".pdf", new byte[] { 0x25, 0x50, 0x44, 0x46 }, true)]
    [InlineData(".pdf", new byte[] { 0x25, 0x50, 0x44 }, false)] // too short for a full signature check
    [InlineData(".pdf", new byte[] { 0x4D, 0x5A, 0x90, 0x00 }, false)] // renamed .exe (MZ header) -> .pdf
    [InlineData(".jpg", new byte[] { 0xFF, 0xD8, 0xFF }, true)]
    [InlineData(".jpeg", new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, true)]
    [InlineData(".jpg", new byte[] { 0xFF, 0xD8 }, false)]
    [InlineData(".png", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, true)]
    [InlineData(".png", new byte[] { 0x89, 0x50, 0x4E, 0x47 }, false)] // only 4 of the required 8 bytes
    [InlineData(".tif", new byte[] { 0x49, 0x49, 0x2A, 0x00 }, true)] // little-endian
    [InlineData(".tiff", new byte[] { 0x4D, 0x4D, 0x00, 0x2A }, true)] // big-endian
    [InlineData(".tif", new byte[] { 0x00, 0x00, 0x00, 0x00 }, false)]
    [InlineData(".exe", new byte[] { 0x4D, 0x5A }, false)] // extension isn't even in the allow-list
    [InlineData(".pdf", new byte[] { }, false)]
    public void IsValidSignature_ChecksMagicNumberAgainstExtension(string extension, byte[] header, bool expected)
    {
        FileTypeHelper.IsValidSignature(header, extension).Should().Be(expected);
    }

    [Theory]
    [InlineData(".pdf", "application/pdf")]
    [InlineData(".jpg", "image/jpeg")]
    [InlineData(".jpeg", "image/jpeg")]
    [InlineData(".png", "image/png")]
    [InlineData(".tif", "image/tiff")]
    [InlineData(".tiff", "image/tiff")]
    [InlineData(".exe", "application/octet-stream")]
    [InlineData("", "application/octet-stream")]
    public void GetContentType_MapsExtensionToMimeType(string extension, string expected)
    {
        FileTypeHelper.GetContentType(extension).Should().Be(expected);
    }
}
