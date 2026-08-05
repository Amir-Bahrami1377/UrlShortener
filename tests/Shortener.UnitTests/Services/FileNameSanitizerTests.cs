using FluentAssertions;
using Shortener.Application.Services;

namespace Shortener.UnitTests.Services;

public sealed class FileNameSanitizerTests
{
    [Theory]
    [InlineData("report\\name.pdf", "reportname.pdf")]
    [InlineData("a/b/c.pdf", "abc.pdf")]
    [InlineData("weird:name*?.pdf", "weirdname.pdf")]
    [InlineData("\"quoted<name>|.pdf\"", "quotedname.pdf")]
    public void Sanitize_ForbiddenChars_AreStripped(string input, string expected)
    {
        FileNameSanitizer.Sanitize(input).Should().Be(expected);
    }

    [Fact]
    public void Sanitize_PersianFileName_IsPreservedUnchanged()
    {
        const string persian = "گزارش فروش شعبه.pdf";

        FileNameSanitizer.Sanitize(persian).Should().Be(persian);
    }

    [Fact]
    public void Sanitize_ControlCharacters_AreStripped()
    {
        var withControlChars = "reportname.pdf";

        FileNameSanitizer.Sanitize(withControlChars).Should().Be("reportname.pdf");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("///\\\\\\")]
    public void Sanitize_ResultingInEmptyString_FallsBackToFile(string input)
    {
        FileNameSanitizer.Sanitize(input).Should().Be("file");
    }

    [Fact]
    public void Sanitize_LeadingAndTrailingWhitespace_IsTrimmed()
    {
        FileNameSanitizer.Sanitize("  report.pdf  ").Should().Be("report.pdf");
    }

    [Fact]
    public void Sanitize_NameLongerThan150Chars_IsTruncatedTo150()
    {
        var longName = new string('a', 200) + ".pdf";

        var result = FileNameSanitizer.Sanitize(longName);

        result.Length.Should().Be(150);
        result.Should().Be(new string('a', 150));
    }

    [Fact]
    public void Sanitize_NameExactlyAtMaxLength_IsUnchanged()
    {
        var exactLength = new string('a', 150);

        FileNameSanitizer.Sanitize(exactLength).Should().Be(exactLength);
    }

    [Fact]
    public void Sanitize_NormalFileName_IsUnchanged()
    {
        FileNameSanitizer.Sanitize("Monthly Report 1405-05.pdf").Should().Be("Monthly Report 1405-05.pdf");
    }
}
