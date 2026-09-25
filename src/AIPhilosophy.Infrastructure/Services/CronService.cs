using AIPhilosophy.Core.Interfaces;

namespace AIPhilosophy.Infrastructure.Services;

public class CronService : ICronService
{
    private static readonly Dictionary<string, int> DayMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SUN"] = 1, ["MON"] = 2, ["TUE"] = 3, ["WED"] = 4, ["THU"] = 5, ["FRI"] = 6, ["SAT"] = 7,
        ["SUNDAY"] = 1, ["MONDAY"] = 2, ["TUESDAY"] = 3, ["WEDNESDAY"] = 4, ["THURSDAY"] = 5, ["FRIDAY"] = 6, ["SATURDAY"] = 7
    };

    public string BuildDailyCron(int hourUtc, int minuteUtc, List<string> daysOfWeek)
    {
        var days = daysOfWeek.Count > 0
            ? string.Join(",", daysOfWeek
                .Where(d => DayMap.ContainsKey(d.Trim()))
                .Select(d => DayMap[d.Trim()])
                .Distinct()
                .OrderBy(x => x))
            : "*";

        return $"0 {Math.Clamp(minuteUtc, 0, 59)} {Math.Clamp(hourUtc, 0, 23)} ? * {days}";
    }

    public List<(int Hour, int Minute)> ParsePostingTimes(string csvTimes)
    {
        var results = new List<(int, int)>();
        if (string.IsNullOrWhiteSpace(csvTimes)) return results;

        foreach (var part in csvTimes.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var segs = part.Split(',', StringSplitOptions.RemoveEmptyEntries);
            foreach (var seg in segs)
            {
                var t = seg.Trim();
                if (TimeSpan.TryParse(t, out var span))
                {
                    results.Add((span.Hours, span.Minutes));
                }
                else
                {
                    var parts = t.Split(':', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2 && int.TryParse(parts[0], out var h) && int.TryParse(parts[1], out var m))
                        results.Add((Math.Clamp(h, 0, 23), Math.Clamp(m, 0, 59)));
                }
            }
        }
        return results.Distinct().ToList();
    }
}