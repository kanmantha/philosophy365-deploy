using System.Text.RegularExpressions;
using AIPhilosophy.Core.Domain;
using AIPhilosophy.Core.Interfaces;
using AIPhilosophy.Infrastructure.Data;
using AIPhilosophy.Infrastructure.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIPhilosophy.Infrastructure.Services;

public class VideoGenerationService(
    AppDbContext db,
    ProviderRegistry registry,
    ITenantService tenantService,
    ILogger<VideoGenerationService> logger)
{
    public async Task<VideoJob> SubmitAsync(Guid scriptId, GenerationProvider? providerOverride = null, CancellationToken ct = default)
    {
        var script = await db.Scripts.FirstOrDefaultAsync(s => s.Id == scriptId, ct)
            ?? throw new InvalidOperationException($"Script {scriptId} not found.");

        if (!await tenantService.CanCreateVideoAsync(script.TenantId, ct))
            throw new InvalidOperationException("Monthly free-tier video limit reached for this workspace.");

        var provider = providerOverride.HasValue
            ? (registry.GetVideoProvider(providerOverride.Value) ?? throw new InvalidOperationException($"Provider {providerOverride} not registered."))
            : (registry.GetPreferredVideoProvider() ?? throw new InvalidOperationException("No video provider configured."));

        var job = new VideoJob
        {
            TenantId = script.TenantId,
            ScriptId = script.Id,
            Provider = provider.Provider,
            Status = VideoStatus.Processing,
            StartedAt = DateTime.UtcNow
        };
        db.VideoJobs.Add(job);
        script.Status = ScriptStatus.Generating;
        await db.SaveChangesAsync(ct);

        try
        {
            var request = new VideoGenerationRequest
            {
                TenantId = script.TenantId,
                ScriptId = script.Id,
                DayNumber = script.DayNumber,
                ScriptText = Clean(script.Body),
                VideoPrompt = script.VideoPrompt,
                Title = script.Title,
                DurationSeconds = script.EstimatedDurationSeconds,
                VoiceStyle = script.VoiceStyle,
                Provider = provider.Provider
            };

            var result = await provider.GenerateAsync(request, ct);

            if (result.Success && !string.IsNullOrEmpty(result.ProviderJobId))
            {
                job.ProviderJobId = result.ProviderJobId;
                job.ProviderResponseJson = result.VideoUrl ?? string.Empty;
                if (result.IsComplete)
                {
                    FinalizeComplete(job, result);
                    await tenantService.RecordVideoUsageAsync(job.TenantId, ct);
                }
            }
            else
            {
                throw new InvalidOperationException(result.Error ?? "Provider returned no job id.");
            }

            await db.SaveChangesAsync(ct);
            return job;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Video generation failed for script {ScriptId}", scriptId);
            job.Status = VideoStatus.Failed;
            job.ErrorMessage = ex.Message;
            job.RetryCount++;
            script.Status = ScriptStatus.Failed;
            await db.SaveChangesAsync(ct);
            throw;
        }
    }

    public async Task<int> PollPendingAsync(CancellationToken ct = default)
    {
        var pending = await db.VideoJobs
            .Where(v => v.Status == VideoStatus.Processing && !string.IsNullOrEmpty(v.ProviderJobId) && v.ProviderJobId.StartsWith("replicate-"))
            .Take(20)
            .ToListAsync(ct);

        var completed = 0;
        foreach (var job in pending)
        {
            var provider = registry.GetVideoProvider(job.Provider);
            if (provider == null) continue;

            var status = await provider.CheckStatusAsync(job.ProviderJobId, ct);
            if (status.Success && status.IsComplete)
            {
                FinalizeComplete(job, new VideoGenerationResult
                {
                    Success = true,
                    VideoUrl = status.VideoUrl,
                    ThumbnailUrl = status.ThumbnailUrl
                });
                await tenantService.RecordVideoUsageAsync(job.TenantId, ct);
                completed++;
                logger.LogInformation("Video job {JobId} completed", job.Id);
            }
            else if (!status.Success && job.RetryCount >= job.MaxRetries)
            {
                job.Status = VideoStatus.Failed;
                job.ErrorMessage = status.Error;
            }
        }

        if (completed > 0) await db.SaveChangesAsync(ct);
        return completed;
    }

    private void FinalizeComplete(VideoJob job, VideoGenerationResult result)
    {
        job.Status = VideoStatus.Ready;
        job.VideoUrl = result.VideoUrl;
        job.ThumbnailUrl = result.ThumbnailUrl;
        job.DurationSeconds = result.DurationSeconds > 0 ? result.DurationSeconds : job.DurationSeconds;
        job.CompletedAt = DateTime.UtcNow;

        var script = db.Scripts.Find(job.ScriptId);
        if (script != null)
        {
            script.Status = ScriptStatus.Generated;
            script.ScheduledPublishDate ??= DateTime.UtcNow;
        }
    }

    private static string Clean(string input)
        => Regex.Replace(Regex.Replace(input ?? string.Empty, @"\[.*?\]", " "), @"\s+", " ").Trim();
}