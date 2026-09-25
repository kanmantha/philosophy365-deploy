using AIPhilosophy.Core.Domain;
using AIPhilosophy.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace AIPhilosophy.Infrastructure.Providers;

/// <summary>
/// Real provider placing a video post on a given platform once OAuth credentials exist.
/// Each platform has its own endpoint/flow; this base emits the payload shape and calls the
/// configured webhook-free client URL. Drop in actual SDK/HTTP flows per platform:
///   - YouTube: videos.insert via Google OAuth (shorts = vertical)
///   - TikTok: POST /v2/post/publish/video/init + status
///   - X (Twitter): media/upload + statuses/update
///   - Instagram: content_publishing_api (+reels_media)
///   - LinkedIn: videos API (ugcPost)
///   - Facebook: /me/videos (Reels)
/// </summary>
public class HttpSocialProvider(ILogger<HttpSocialProvider> logger) : ISocialProvider
{
    private readonly ILogger<HttpSocialProvider> _logger = logger;

    public SocialPlatform Platform => SocialPlatform.None;
    public bool IsConfigured => false;

    public Task ValidateAccountAsync(SocialAccount account, CancellationToken ct)
    {
        _logger.LogInformation("Http provider: validating {Platform} account {Account}", account.Platform, account.AccountHandle);
        throw new NotSupportedException("Real platform upload requires OAuth client credentials. Enable it by providing credentials and implementing the platform flow.");
    }

    public Task<PostResult> PostVideoAsync(PostRequest request, SocialAccount account, CancellationToken ct)
        => throw new NotSupportedException($"Real publishing for {request.Platform} is not configured. Use Mock provider in demo mode.");
}