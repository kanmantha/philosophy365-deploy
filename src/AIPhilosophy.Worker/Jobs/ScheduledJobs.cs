using AIPhilosophy.Core.Domain;
using AIPhilosophy.Core.Interfaces;
using AIPhilosophy.Infrastructure.Data;
using AIPhilosophy.Infrastructure.Services;
using AIPhilosophy.Worker.Pipeline;
using Microsoft.Extensions.Logging;
using Quartz;

namespace AIPhilosophy.Worker.Jobs;

[DisallowConcurrentExecution]
public class PipelineJob(
    IServiceScopeFactory scopeFactory,
    ILogger<PipelineJob> logger) : IJob
{
    public async ValueTask Execute(IJobExecutionContext context, CancellationToken token)
    {
        using var scope = scopeFactory.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<DailyPipelineRunner>();
        var ran = await runner.RunAsync(context.CancellationToken);
        if (ran > 0)
            logger.LogInformation("Pipeline job completed for {Count} tenant(s)", ran);
    }
}

[DisallowConcurrentExecution]
public class PublishingJob(
    IServiceScopeFactory scopeFactory,
    ILogger<PublishingJob> logger) : IJob
{
    public async ValueTask Execute(IJobExecutionContext context, CancellationToken token)
    {
        using var scope = scopeFactory.CreateScope();
        var tweets = scope.ServiceProvider.GetRequiredService<SocialPosterService>();
        var videos = scope.ServiceProvider.GetRequiredService<VideoGenerationService>();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantService>();

        var published = await tweets.PublishDueAsync(context.CancellationToken);
        var requeued = await tweets.RetryFailedAsync(context.CancellationToken);
        var polled = await videos.PollPendingAsync(context.CancellationToken);

        logger.LogInformation(
            "Publishing sweep: published={Published}, requeued={Requeued}, pollsCompleted={Polled}",
            published, requeued, polled);
    }
}

[DisallowConcurrentExecution]
public class MonthlyResetJob(
    IServiceScopeFactory scopeFactory,
    ILogger<MonthlyResetJob> logger) : IJob
{
    public async ValueTask Execute(IJobExecutionContext context, CancellationToken token)
    {
        using var scope = scopeFactory.CreateScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantService>();
        await tenant.ResetMonthlyUsageAsync(context.CancellationToken);
        logger.LogInformation("Monthly free-tier usage reset completed.");
    }
}

[DisallowConcurrentExecution]
public class CleanupJob(IServiceScopeFactory scopeFactory, ILogger<CleanupJob> logger) : IJob
{
    public async ValueTask Execute(IJobExecutionContext context, CancellationToken token)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var cutoff = DateTime.UtcNow.AddDays(-90);
        var staleLogs = db.SystemLogs.Where(l => l.CreatedAt < cutoff).Take(5000);
        db.RemoveRange(staleLogs);

        var failedOld = db.Posts
            .Where(p => p.Status == PostStatus.Failed && p.RetryCount >= 3 && p.PostedAt == null)
            .ToList();
        foreach (var p in failedOld) p.Status = PostStatus.Skipped;

        await db.SaveChangesAsync(context.CancellationToken);
        logger.LogInformation("Cleanup sweep done ({FailedPosts} archived).", failedOld.Count);
    }
}

