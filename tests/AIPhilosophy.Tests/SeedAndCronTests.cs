using AIPhilosophy.Infrastructure.Seed;
using AIPhilosophy.Infrastructure.Services;

namespace AIPhilosophy.Tests;

public class SeedContentTests
{
    [Fact]
    public void Build365_ReturnsExactlyOneScriptForEveryDay()
    {
        var scripts = SeedContent.Build365();
        Assert.Equal(365, scripts.Count);
        Assert.Equal(Enumerable.Range(1, 365), scripts.Select(s => s.Day));
    }

    [Fact]
    public void Build365_First30AreHandcraftedAndComplete()
    {
        var scripts = SeedContent.Build365().OrderBy(s => s.Day).Take(30).ToList();
        Assert.All(scripts, s =>
        {
            Assert.False(string.IsNullOrWhiteSpace(s.Title));
            Assert.False(string.IsNullOrWhiteSpace(s.Philosopher));
            Assert.False(string.IsNullOrWhiteSpace(s.Body));
            Assert.False(string.IsNullOrWhiteSpace(s.VideoPrompt));
            Assert.False(string.IsNullOrWhiteSpace(s.Description));
            Assert.Contains("#", s.Hashtags);
            Assert.True(s.Body.Length > 120, $"Day {s.Day} body too short");
        });
    }

    [Fact]
    public void Build365_NoDuplicateTitlesAcrossFullYear()
    {
        var titles = SeedContent.Build365().Select(s => s.Title.ToLowerInvariant()).ToList();
        Assert.Equal(titles.Count, titles.Distinct().Count());
    }
}

public class CronServiceTests
{
    private readonly CronService _cron = new();

    [Fact]
    public void BuildDailyCron_MapsMonTueToQuartzCivilDays()
    {
        var cron = _cron.BuildDailyCron(6, 0, new List<string> { "Mon", "Tue" });
        Assert.Equal("0 0 6 ? * 2,3", cron);
    }

    [Fact]
    public void BuildDailyCron_EmptyDaysProducesEveryDay()
    {
        var cron = _cron.BuildDailyCron(9, 15, new List<string>());
        Assert.Equal("0 15 9 ? * *", cron);
    }

    [Theory]
    [InlineData("09:00", 9, 0)]
    [InlineData("6:30,18:30", 18, 30)]
    [InlineData("09:00;12:00", 12, 0)]
    [InlineData("", -1, -1)] // empty -> ignored
    public void ParsePostingTimes_HandlesCsvAndSemicolons(string input, int expectedHour, int expectedMinute)
    {
        var times = _cron.ParsePostingTimes(input);
        if (expectedHour < 0)
        {
            Assert.Empty(times);
            return;
        }
        Assert.Contains(times, t => t.Hour == expectedHour && t.Minute == expectedMinute);
    }

    [Fact]
    public void ParsePostingTimes_DeduplicatesAndClamps()
    {
        var times = _cron.ParsePostingTimes("09:00;9:00;99:00").OrderBy(t => t.Hour).ToList();
        Assert.Equal(9, times[0].Hour); // "9:00" and "09:00" collapse to one
        Assert.Equal(23, times[^1].Hour); // 99 clamps to 23
        Assert.Equal(1, times.Count(t => t.Hour == 9 && t.Minute == 0));
    }
}