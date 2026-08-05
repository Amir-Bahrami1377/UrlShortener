using FluentAssertions;
using Shortener.Infrastructure.Retention;

namespace Shortener.UnitTests.Retention;

public sealed class JobWindowTests
{
    private static RetentionJobOptions Options() => new() { JobWindowStartHour = 1, JobWindowEndHour = 5 };

    [Theory]
    [InlineData(0, 59, false)]
    [InlineData(1, 0, true)]
    [InlineData(3, 0, true)]
    [InlineData(4, 59, true)]
    [InlineData(5, 0, false)]
    [InlineData(12, 0, false)]
    public void IsWithin_MatchesTheConfiguredStartAndEndHour(int hour, int minute, bool expected)
    {
        var now = new DateTime(2026, 8, 4, hour, minute, 0);

        JobWindow.IsWithin(now, Options()).Should().Be(expected);
    }

    [Fact]
    public void TimeUntilNextWindow_BeforeTodaysWindow_ReturnsDelayUntilTodaysStart()
    {
        var now = new DateTime(2026, 8, 4, 0, 30, 0);

        var delay = JobWindow.TimeUntilNextWindow(now, windowStartHour: 1);

        delay.Should().Be(TimeSpan.FromMinutes(30));
    }

    [Fact]
    public void TimeUntilNextWindow_AfterTodaysWindow_ReturnsDelayUntilTomorrow()
    {
        var now = new DateTime(2026, 8, 4, 6, 0, 0);

        var delay = JobWindow.TimeUntilNextWindow(now, windowStartHour: 1);

        delay.Should().Be(TimeSpan.FromHours(19));
    }

    [Fact]
    public void TimeUntilNextWindow_NeverReturnsANonPositiveDuration()
    {
        var now = new DateTime(2026, 8, 4, 1, 0, 0);

        var delay = JobWindow.TimeUntilNextWindow(now, windowStartHour: 1);

        delay.Should().BePositive();
    }
}
