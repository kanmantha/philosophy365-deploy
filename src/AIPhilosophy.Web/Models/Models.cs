namespace AIPhilosophy.Web.Models;

public record PageResult<T>(int total, IReadOnlyList<T> items);

public record ScriptDto(
    Guid id, int dayNumber, string title, string subtitle, string category, string philosopher,
    string body, string videoPrompt, string description, string hashtags,
    string status, int estimatedDurationSeconds, string? scheduledPublishDate);

public record VideoDto(
    Guid id, Guid? scriptId, int day, string title, string provider, string status,
    string? videoUrl, string? thumbnailUrl, int durationSeconds, int retryCount,
    string? errorMessage, DateTime createdAt, DateTime? completedAt);

public record VideoStats(int total, int generated, int failed, int processing);

public record PostDto(
    Guid id, string platform, string status, string accountHandle, int day, string title,
    string caption, string? postUrl, string? errorMessage, DateTime? scheduledAt, DateTime? postedAt, int retryCount);

public record SocialAccountDto(
    Guid id, string platform, string accountName, string accountHandle,
    bool isDefaultForPlatform, string status, bool hasToken, DateTime? lastVerifiedAt);

public record PlatformDto(
    string platform, string name, bool isMock, bool isConfigured,
    bool requiresSetup, bool hasLinkedToken, bool simulated, string mode);

public record OAuthStartDto(string authorizeUrl);

public record ScheduleRuleDto(
    Guid id, string name, bool isEnabled, int generationHourUTC, int postHourUTC,
    string postingDaysCsv, string postingTimesCsv, string targetPlatformsCsv, string timeZoneId, string generationCron)
{
    public Guid id { get; set; } = id;
    public string name { get; set; } = name;
    public bool isEnabled { get; set; } = isEnabled;
    public int generationHourUTC { get; set; } = generationHourUTC;
    public int postHourUTC { get; set; } = postHourUTC;
    public string postingDaysCsv { get; set; } = postingDaysCsv;
    public string postingTimesCsv { get; set; } = postingTimesCsv;
    public string targetPlatformsCsv { get; set; } = targetPlatformsCsv;
    public string timeZoneId { get; set; } = timeZoneId;
    public string generationCron { get; set; } = generationCron;
}

public record VideoStatsResult(VideoStats Data);

public record LoginResponse(string token, string email, string displayName, Guid tenantId, string? tier, int videosRemaining);