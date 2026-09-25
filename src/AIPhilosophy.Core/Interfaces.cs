using AIPhilosophy.Core.Domain;

namespace AIPhilosophy.Core.Interfaces;

public interface IVideoGenerationService
{
    Task<VideoGenerationResult> GenerateAsync(VideoGenerationRequest request, CancellationToken ct = default);
    Task<GenerationStatusResult> CheckStatusAsync(string providerJobId, CancellationToken ct = default);
}

public class VideoGenerationRequest
{
    public Guid TenantId { get; set; }
    public Guid ScriptId { get; set; }
    public int DayNumber { get; set; }
    public required string ScriptText { get; set; }
    public required string VideoPrompt { get; set; }
    public required string Title { get; set; }
    public int DurationSeconds { get; set; } = 45;
    public GenerationProvider Provider { get; set; }
    public required string VoiceStyle { get; set; }
}

public class VideoGenerationResult
{
    public bool Success { get; set; }
    public bool IsComplete { get; set; }
    public string? ProviderJobId { get; set; }
    public string? VideoUrl { get; set; }
    public string? ThumbnailUrl { get; set; }
    public int DurationSeconds { get; set; }
    public string? Error { get; set; }
}

public class GenerationStatusResult
{
    public bool Success { get; set; }
    public bool IsComplete { get; set; }
    public string? VideoUrl { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? Error { get; set; }
}

public interface ISocialPosterService
{
    Task<PostResult> PostVideoAsync(PostRequest request, CancellationToken ct = default);
    Task ValidateAccountAsync(SocialAccount account, CancellationToken ct = default);
}

public class PostRequest
{
    public Guid TenantId { get; set; }
    public Guid SocialAccountId { get; set; }
    public SocialPlatform Platform { get; set; }
    public required string VideoUrl { get; set; }
    public required string Title { get; set; }
    public required string Description { get; set; }
    public required string Caption { get; set; }
    public required string Hashtags { get; set; }
    public string? ThumbnailUrl { get; set; }
}

public class PostResult
{
    public bool Success { get; set; }
    public string? PostUrl { get; set; }
    public string? Error { get; set; }
}

public interface ITenantService
{
    Task<Tenant> GetTenantAsync(Guid tenantId, CancellationToken ct = default);
    Task<bool> CanCreateVideoAsync(Guid tenantId, CancellationToken ct = default);
    Task<bool> CanPostAsync(Guid tenantId, CancellationToken ct = default);
    Task RecordVideoUsageAsync(Guid tenantId, CancellationToken ct = default);
    Task ResetMonthlyUsageAsync(CancellationToken ct = default);
}