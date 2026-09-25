using AIPhilosophy.Core.Domain;
using AIPhilosophy.Infrastructure.Data;
using AIPhilosophy.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIPhilosophy.Worker.Pipeline;

/// <summary>
/// Runs the daily content pipeline for every tenant with an enabled schedule rule:
/// pick the next philosophy script, generate its AI video, then auto-queue posts
/// on configured platforms at configured local posting times.
/// </summary>
public class DailyPipelineRunner(
    AppDbContext db,
    VideoGenerationService videoService,
    SocialPosterService posterService,
    ILogger<DailyPipelineRunner> logger)
{
    public async Task<int> RunAsync(CancellationToken ct)
    {
        var rules = await db.ScheduleRules
            .Where(r => r.IsEnabled)
            .ToListAsync(ct);

        var ran = 0;
        foreach (var rule in rules)
        {
            if (ct.IsCancellationRequested) break;

            var hour = rule.GenerationHourUTC;
            var lastRun = rule.LastPipelineRunAt;
            var isNewDay = lastRun == null
                || lastRun.Value.Date < DateTime.UtcNow.Date
                || (lastRun.Value.Date == DateTime.UtcNow.Date && lastRun.Value.Hour != hour);

            if (!isNewDay || DateTime.UtcNow.Hour != hour)
                continue;

            rule.LastPipelineRunAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            try
            {
                var worked = await RunForRuleAsync(rule, ct);
                logger.LogInformation("Daily pipeline for tenant {TenantId} generated {Count} item(s)", rule.TenantId, worked);
                ran++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Daily pipeline failed for tenant {TenantId}", rule.TenantId);
            }
        }
        return ran;
    }

    private async Task<int> RunForRuleAsync(ScheduleRule rule, CancellationToken ct)
    {
        var platforms = rule.TargetPlatformsCsv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => Enum.TryParse<SocialPlatform>(p, true, out var sp) ? sp : SocialPlatform.None)
            .Where(p => p != SocialPlatform.None)
            .ToList();

        if (platforms.Count == 0) return 0;

        var now = DateTime.UtcNow;
        var candidate = await db.Scripts
            .Where(s => s.TenantId == rule.TenantId
                        && (s.Status == ScriptStatus.Ready
                            || s.Status == ScriptStatus.Draft
                            || s.Status == ScriptStatus.Failed)
                        && (s.ScheduledPublishDate == null || s.ScheduledPublishDate <= now.AddDays(2)))
            .OrderBy(s => s.DayNumber)
            .FirstOrDefaultAsync(ct);

        if (candidate == null) return 0;

        VideoJob? job = null;
        try
        {
            job = await videoService.SubmitAsync(candidate.Id, null, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Skipping video generation for script {ScriptId} ({Title})", candidate.Id, candidate.Title);
            return 0;
        }

        if (job == null || job.Status != VideoStatus.Ready) return 0;

        var times = new CronServiceParsed().Parse(rule.PostingTimesCsv);
        var queued = 0;
        foreach (var platform in platforms)
        {
            var scheduledAt = NextOccurrence(times, now);
            try
            {
                await posterService.QueueAsync(job.Id, platform, scheduledAt, ct);
                queued++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Queue post to {Platform} failed for video {JobId}", platform, job.Id);
            }
        }
        return queued;
    }

    private static DateTime NextOccurrence(List<TimeOnly> times, DateTime now)
    {
        var today = DateOnly.FromDateTime(now);
        foreach (var t in times.OrderBy(x => x))
        {
            var candidate = new DateTime(today.Year, today.Month, today.Day, t.Hour, t.Minute, 0, DateTimeKind.Utc);
            if (candidate > now) return candidate;
        }
        var tomorrow = today.AddDays(1);
        var first = times.OrderBy(x => x).First();
        return new DateTime(tomorrow.Year, tomorrow.Month, tomorrow.Day, first.Hour, first.Minute, 0, DateTimeKind.Utc);
    }

    private class CronServiceParsed
    {
        public List<TimeOnly> Parse(string csvTimes)
        {
            var list = new List<TimeOnly>();
            foreach (var part in csvTimes.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                foreach (var seg in part.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var segs = seg.Split(':');
                    if (segs.Length >= 2 && int.TryParse(segs[0], out var h) && int.TryParse(segs[1], out var m))
                        list.Add(new TimeOnly(Math.Clamp(h, 0, 23), Math.Clamp(m, 0, 59)));
                }
            }
            return list.Distinct().ToList();
        }
    }
}
