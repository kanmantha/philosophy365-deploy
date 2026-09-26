namespace AIPhilosophy.Api.Models;

public static class RequestModels
{
    public record RegisterRequest(string Email, string Password, string? DisplayName);
    public record LoginRequest(string Email, string Password);
    public record RefreshTokenRequest(string Token);

    public record UserRoleRequest(string Role);
    public record ResetPasswordRequest(string NewPassword);
    public record CreateUserRequest(string Email, string Password, string? DisplayName, string? Role, string? TenantName);
    public record UpdateUserRequest(string? DisplayName);

    public record ScriptDto(
        Guid Id,
        int DayNumber,
        string Title,
        string Subtitle,
        string Category,
        string Philosopher,
        string Body,
        string VideoPrompt,
        string Description,
        string Hashtags,
        string Status,
        int EstimatedDurationSeconds,
        string? ScheduledPublishDate);

    public record ScriptUpsertRequest(
        int DayNumber,
        string Title,
        string Subtitle,
        string Category,
        string Philosopher,
        string Body,
        string VideoPrompt,
        string Description,
        string Hashtags,
        int EstimatedDurationSeconds,
        string? ScheduledPublishDate);

    public record ScriptCustomRequest(
        string? Title,
        string Body,
        string? Philosopher,
        string? Category,
        string? VideoPrompt,
        string? Description,
        string? Hashtags,
        int? DayNumber,
        string? ScheduledPublishDate);

    public record GenerateVideoRequest(Guid ScriptId, string? Provider);
    public record QueuePostRequest(Guid VideoJobId, string Platform, string? ScheduledAt);

    public record SocialAccountRequest(
        string Platform,
        string AccountName,
        string AccountHandle,
        string? AccessToken,
        string? RefreshToken,
        string? TokenSecret,
        bool IsDefaultForPlatform);

    public record OAuthStartRequest(string Platform);

    public record ScheduleRuleRequest(
        string Name,
        int GenerationHourUTC,
        int PostHourUTC,
        string PostingDaysCsv,
        string PostingTimesCsv,
        string TargetPlatformsCsv,
        bool IsEnabled,
        string? TimeZoneId);
}

public record ApiResponse<T>(bool Success, string? Message, T? Data)
{
    public static ApiResponse<T> Ok(T data, string? message = null) => new(true, message, data);
    public static ApiResponse<T> Fail(string message) => new(false, message, default);
}