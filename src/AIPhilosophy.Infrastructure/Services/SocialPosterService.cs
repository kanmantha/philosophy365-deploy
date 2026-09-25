using AIPhilosophy.Core.Domain;
using AIPhilosophy.Core.Interfaces;
using AIPhilosophy.Infrastructure.Data;
using AIPhilosophy.Infrastructure.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AIPhilosophy.Infrastructure.Services;

public class SocialPosterService(
    AppDbContext db,
    ProviderRegistry registry,
    IConfiguration config,
    ILogger<SocialPosterService> logger)
{
    private bool DemoModeEnabled => string.Equals(config["Social:DemoMode"], "true", StringComparison.OrdinalIgnoreCase);
    /// <summary>Queue a post for a video across the workspace's default platform accounts.</summary>
    public async Task<Post> QueueAsync(Guid videoJobId, SocialPlatform platform, DateTime? scheduledAt = null, CancellationToken ct = default)
    {
        var video = await db.VideoJobs
            .Include(v => v.Script)
            .FirstOrDefaultAsync(v => v.Id == videoJobId, ct)
            ?? throw new InvalidOperationException($"Video {videoJobId} not found.");

        var account = await db.SocialAccounts
            .FirstOrDefaultAsync(a => a.TenantId == video.TenantId && a.Platform == platform && a.IsDefaultForPlatform && a.Status == SocialAccountStatus.Active, ct)
            ?? await db.SocialAccounts
                .FirstOrDefaultAsync(a => a.TenantId == video.TenantId && a.Platform == platform && a.Status == SocialAccountStatus.Active, ct);

        if (account == null)
            throw new InvalidOperationException($"No active {platform} account linked to this workspace. Connect one first.");

        var post = new Post
        {
            TenantId = video.TenantId,
            SocialAccountId = account.Id,
            VideoJobId = video.Id,
            ScriptId = video.ScriptId,
            Platform = platform,
            Status = scheduledAt.HasValue ? PostStatus.Scheduled : PostStatus.Pending,
            Caption = BuildCaption(video.Script),
            ScheduledAt = scheduledAt
        };

        db.Posts.Add(post);
        if (video.Script != null) video.Script.Status = ScriptStatus.Scheduled;
        video.Status = VideoStatus.Posting;
        await db.SaveChangesAsync(ct);
        return post;
    }

    public async Task<int> PublishDueAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var due = await db.Posts
            .Include(p => p.SocialAccount)
            .Include(p => p.VideoJob)
            .Include(p => p.Script)
            .Where(p => (p.Status == PostStatus.Pending && p.ScheduledAt == null)
                     || (p.Status == PostStatus.Scheduled && p.ScheduledAt <= now))
            .Take(50)
            .ToListAsync(ct);

        var published = 0;
        foreach (var post in due)
        {
            if (post.VideoJob == null || string.IsNullOrEmpty(post.VideoJob.VideoUrl))
            {
                post.Status = PostStatus.Failed;
                post.ErrorMessage = "No video available on job.";
                continue;
            }

            var realProvider = registry.GetSocialProvider(post.Platform);
            var provider = realProvider;
            if (DemoModeEnabled && realProvider != null && !realProvider.IsConfigured)
                provider = registry.GetSocialProvider(SocialPlatform.None);

            if (provider == null || post.SocialAccount == null)
            {
                post.Status = PostStatus.Failed;
                post.ErrorMessage = realProvider == null
                    ? $"No social provider registered for {post.Platform}."
                    : $"Platform {post.Platform} is wired for real posting but has no credentials yet; set Social:{post.Platform}:* (or Social:DemoMode=true to simulate).";
                continue;
            }

            try
            {
                var request = new PostRequest
                {
                    TenantId = post.TenantId,
                    SocialAccountId = post.SocialAccount.Id,
                    Platform = post.Platform,
                    VideoUrl = post.VideoJob.VideoUrl,
                    Title = post.Script?.Title ?? string.Empty,
                    Description = post.Script?.Description ?? string.Empty,
                    Caption = post.Caption,
                    Hashtags = post.Script?.Hashtags ?? string.Empty,
                    ThumbnailUrl = post.VideoJob.ThumbnailUrl
                };

                post.Status = PostStatus.Posting;
                await db.SaveChangesAsync(ct);

                var result = await provider.PostVideoAsync(request, post.SocialAccount, ct);

                if (result.Success)
                {
                    post.Status = PostStatus.Posted;
                    post.PostUrl = result.PostUrl;
                    post.PostedAt = DateTime.UtcNow;
                    if (post.Script != null)
                    {
                        post.Script.Status = ScriptStatus.Published;
                        post.Script.ScheduledPublishDate ??= DateTime.UtcNow;
                    }
                }
                else
                {
                    post.Status = PostStatus.Failed;
                    post.ErrorMessage = result.Error;
                    post.RetryCount++;
                }
                published++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Publishing post {PostId} to {Platform} failed", post.Id, post.Platform);
                post.Status = PostStatus.Failed;
                post.ErrorMessage = ex.Message;
                post.RetryCount++;
            }
        }

        if (due.Count > 0) await db.SaveChangesAsync(ct);
        return published;
    }

    public async Task<int> RetryFailedAsync(CancellationToken ct = default)
    {
        var failed = await db.Posts
            .Where(p => p.Status == PostStatus.Failed && p.RetryCount < 3 && p.PostedAt == null)
            .Take(30)
            .ToListAsync(ct);

        foreach (var p in failed)
            p.Status = PostStatus.Pending;

        if (failed.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Re-queued {Count} failed posts for retry", failed.Count);
        }
        return failed.Count;
    }

    private static string BuildCaption(Script? script)
    {
        if (script == null) return string.Empty;
        var sb = new System.Text.StringBuilder();
        sb.Append(script.Description);
        if (!string.IsNullOrWhiteSpace(script.Hashtags))
            sb.Append("\n\n").Append(script.Hashtags);
        return sb.ToString();
    }
}