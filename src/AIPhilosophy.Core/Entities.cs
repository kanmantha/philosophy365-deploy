using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace AIPhilosophy.Core.Domain;

public class Tenant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public SubscriptionTier Tier { get; set; } = SubscriptionTier.Free;
    public bool BypassUsageLimits { get; set; }
    public int DailyVideoLimit { get; set; } = 3;
    public int DailyPostLimit { get; set; } = 10;
    public int TotalVideoLimit { get; set; } = 365;
    public int VideosUsedThisMonth { get; set; }
    public DateTime TierRenewalDate { get; set; } = DateTime.UtcNow.AddMonths(1);

    public ICollection<ApplicationUser> Users { get; set; } = new List<ApplicationUser>();
    public ICollection<Script> Scripts { get; set; } = new List<Script>();
    public ICollection<SocialAccount> SocialAccounts { get; set; } = new List<SocialAccount>();
    public ICollection<VideoJob> VideoJobs { get; set; } = new List<VideoJob>();
}

public class ApplicationUser : IdentityUser
{
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Script
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    public int DayNumber { get; set; }
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(500)]
    public string Subtitle { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string VideoPrompt { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string Description { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string Hashtags { get; set; } = string.Empty;

    [MaxLength(200)]
    public string Category { get; set; } = string.Empty;

    [MaxLength(200)]
    public string Philosopher { get; set; } = string.Empty;

    public int EstimatedDurationSeconds { get; set; } = 45;

    [MaxLength(100)]
    public string VoiceStyle { get; set; } = "Warm, Calm, Reflective";

    [MaxLength(500)]
    public string? BackgroundMusic { get; set; }

    public ScriptStatus Status { get; set; } = ScriptStatus.Draft;
    public bool IsFeatured { get; set; }
    public DateTime? ScheduledPublishDate { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<VideoJob> VideoJobs { get; set; } = new List<VideoJob>();
    public ICollection<Post> Posts { get; set; } = new List<Post>();
}

public class SocialAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    public SocialPlatform Platform { get; set; }
    [MaxLength(200)]
    public string AccountName { get; set; } = string.Empty;
    [MaxLength(500)]
    public string AccountHandle { get; set; } = string.Empty;
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public string? TokenSecret { get; set; }
    public string? ProfileJson { get; set; }
    public SocialAccountStatus Status { get; set; } = SocialAccountStatus.Active;
    public bool IsDefaultForPlatform { get; set; }
    public DateTime? TokenExpiry { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastVerifiedAt { get; set; }

    public ICollection<Post> Posts { get; set; } = new List<Post>();
}

public class VideoJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }
    public Guid? ScriptId { get; set; }
    public Script? Script { get; set; }

    public GenerationProvider Provider { get; set; } = GenerationProvider.Mock;
    [MaxLength(500)]
    public string ProviderJobId { get; set; } = string.Empty;
    public string? ProviderResponseJson { get; set; }
    public VideoStatus Status { get; set; } = VideoStatus.Pending;
    public int RetryCount { get; set; }
    public int MaxRetries { get; set; } = 3;

    [MaxLength(1000)]
    public string? VideoUrl { get; set; }
    [MaxLength(1000)]
    public string? ThumbnailUrl { get; set; }
    public int DurationSeconds { get; set; }

    [MaxLength(2000)]
    public string? ErrorMessage { get; set; }

    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Post> Posts { get; set; } = new List<Post>();
}

public class Post
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }
    public Guid? SocialAccountId { get; set; }
    public SocialAccount? SocialAccount { get; set; }
    public Guid? VideoJobId { get; set; }
    public VideoJob? VideoJob { get; set; }
    public Guid? ScriptId { get; set; }
    public Script? Script { get; set; }

    public SocialPlatform Platform { get; set; }
    public PostStatus Status { get; set; } = PostStatus.Pending;
    public int RetryCount { get; set; }

    [MaxLength(2000)]
    public string Caption { get; set; } = string.Empty;
    [MaxLength(2000)]
    public string? PostUrl { get; set; }
    [MaxLength(2000)]
    public string? ErrorMessage { get; set; }

    public DateTime? ScheduledAt { get; set; }
    public DateTime? PostedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class SystemLog
{
    public long Id { get; set; }
    public Guid? TenantId { get; set; }
    public string Level { get; set; } = "Info";
    public string Source { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Details { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class ScheduleRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;

    public int GenerationHourUTC { get; set; } = 6;
    public int PostHourUTC { get; set; } = 12;

    public string PostingDaysCsv { get; set; } = "Mon,Tue,Wed,Thu,Fri,Sat,Sun";
    public string PostingTimesCsv { get; set; } = "09:00,12:00,18:00;09:00,12:00,18:00";
    public string TargetPlatformsCsv { get; set; } = "YouTubeShorts,TikTok,InstagramReels";

    [MaxLength(200)]
    public string TimeZoneId { get; set; } = "UTC";
    public DateTime? LastPipelineRunAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}