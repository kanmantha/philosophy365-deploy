using AIPhilosophy.Core.Domain;
using AIPhilosophy.Core.Interfaces;
using AIPhilosophy.Infrastructure.Data;
using AIPhilosophy.Infrastructure.Providers;
using AIPhilosophy.Infrastructure.Services;
using AIPhilosophy.Worker.Jobs;
using AIPhilosophy.Worker.Pipeline;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quartz;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .WriteTo.File(Path.Combine(AppContext.BaseDirectory, "logs", "worker-.log"), rollingInterval: RollingInterval.Day)
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddSerilog();

builder.Services.AddAppDbContext(builder.Configuration);
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient();

builder.Services.AddSingleton<ICronService, CronService>();
builder.Services.AddScoped<ITenantService, TenantService>();
builder.Services.AddScoped<VideoGenerationService>();
builder.Services.AddScoped<SocialPosterService>();
builder.Services.AddScoped<DailyPipelineRunner>();

builder.Services.AddSingleton<IVideoProvider, MockVideoProvider>();
builder.Services.AddSingleton<IVideoProvider, ReplicateVideoProvider>();
builder.Services.AddSingleton<ISocialProvider, MockSocialProvider>();
builder.Services.AddSingleton<ISocialProvider, YouTubeSocialProvider>();
builder.Services.AddSingleton<ISocialProvider, TikTokSocialProvider>();
builder.Services.AddSingleton<ISocialProvider, InstagramSocialProvider>();
builder.Services.AddSingleton<ISocialProvider, XSocialProvider>();
builder.Services.AddSingleton<ISocialProvider, LinkedInSocialProvider>();
builder.Services.AddSingleton<ISocialProvider, FacebookSocialProvider>();
builder.Services.AddSingleton<ProviderRegistry>();

var jobDelay = builder.Configuration.GetValue<int>("Scheduler:StartupDelaySeconds", 15);

builder.Services.AddQuartz(q =>
{

    q.AddJob<PipelineJob>(j => j.WithIdentity("daily-pipeline"));
    q.AddJob<PublishingJob>(j => j.WithIdentity("publishing"));
    q.AddJob<MonthlyResetJob>(j => j.WithIdentity("monthly-reset"));
    q.AddJob<CleanupJob>(j => j.WithIdentity("cleanup"));

    q.AddTrigger(t => t
        .ForJob("daily-pipeline")
        .WithIdentity("daily-pipeline-trigger")
        .StartAt(DateTimeOffset.UtcNow.AddSeconds(jobDelay))
        .WithCronSchedule("0 0 * * * ?"));

    q.AddTrigger(t => t
        .ForJob("publishing")
        .WithIdentity("publishing-trigger")
        .StartAt(DateTimeOffset.UtcNow.AddSeconds(30))
        .WithCronSchedule("0 0/3 * * * ?"));

    q.AddTrigger(t => t
        .ForJob("monthly-reset")
        .WithIdentity("monthly-reset-trigger")
        .StartAt(DateTimeOffset.UtcNow.AddSeconds(jobDelay + 5))
        .WithCronSchedule("0 15 0 1 * ?"));

    q.AddTrigger(t => t
        .ForJob("cleanup")
        .WithIdentity("cleanup-trigger")
        .StartAt(DateTimeOffset.UtcNow.AddSeconds(jobDelay + 10))
        .WithCronSchedule("0 30 3 * * ?"));
});

builder.Services.AddQuartzHostedService(o => o.WaitForJobsToComplete = true);
builder.Services.AddHealthChecks();

var host = builder.Build();

using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        await db.EnsureSchemaAsync(!builder.Configuration.UsesPostgres(), logger);
        logger.LogInformation("Worker database schema ready.");
    }
    catch (Exception ex)
    {
        logger.LogCritical(ex, "Worker schema initialization failed; scheduled jobs will retry.");
    }
}

host.MapGet("/", () => Results.Ok(new { service = "365-philosophy-worker", status = "running" }));
host.MapHealthChecks("/healthz");

host.Run();

public partial class Program { }