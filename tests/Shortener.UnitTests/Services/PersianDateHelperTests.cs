using FluentAssertions;
using Shortener.Application.Services;

namespace Shortener.UnitTests.Services;

public sealed class PersianDateHelperTests
{
    [Fact]
    public void ToPersianDate_KnownUtcInstant_ConvertsCorrectly()
    {
        // 2026-08-04 is 1405/05/13 on the Persian calendar (Tehran is UTC+3:30, so this UTC
        // midday instant stays on the same Persian calendar day after the timezone shift).
        var utc = new DateTime(2026, 8, 4, 12, 0, 0, DateTimeKind.Utc);

        PersianDateHelper.ToPersianDate(utc).Should().Be("1405/05/13");
    }

    [Fact]
    public void ParsePersianDate_ThenToPersianDate_RoundTrips()
    {
        var parsed = PersianDateHelper.ParsePersianDate("1405/05/13");

        parsed.Should().NotBeNull();
        PersianDateHelper.ToPersianDate(parsed!.Value).Should().Be("1405/05/13");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-date")]
    [InlineData("1405/13/40")]
    public void ParsePersianDate_InvalidInput_ReturnsNull(string? input)
    {
        PersianDateHelper.ParsePersianDate(input).Should().BeNull();
    }

    [Fact]
    public void ToPersianDate_NullableExtension_ReturnsDashForNull()
    {
        DateTime? nullDate = null;

        nullDate.ToPersianDate().Should().Be("—");
    }

    [Fact]
    public void ToPersianDateTime_KnownUtcInstant_AppendsLocalClockTime()
    {
        // Tehran is UTC+3:30, so 12:00 UTC is 15:30 local.
        var utc = new DateTime(2026, 8, 4, 12, 0, 0, DateTimeKind.Utc);

        PersianDateHelper.ToPersianDateTime(utc).Should().Be("1405/05/13 15:30");
    }

    [Fact]
    public void ToPersianDateTime_NullableExtension_ReturnsDashForNull()
    {
        DateTime? nullDate = null;

        nullDate.ToPersianDateTime().Should().Be("—");
    }
}
