using System.Net.Http.Headers;
using AIPhilosophy.Core.Domain;
using AIPhilosophy.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AIPhilosophy.Infrastructure.Providers;

/// <summary>Real TikTok provider. Uses the TikTok Content Posting API (init -> file upload -> status -> done).</summary>
public class TikTokSocialProvider : RealSocialProviderBase, ISocialProvider
{
    public TikTokSocialProvider(IConfiguration config, HttpClient http, ILogger<TikTokSocialProvider> logger)
        : base(config, http, logger, "TikTok")
    {
    }

    public SocialPlatform Platform => SocialPlatform.TikTok;

    public Task ValidateAccountAsync(SocialAccount account, CancellationToken ct)
        => ValidateBearerTokenAsync(account.AccessToken, "https://open.tiktokapis.com/v2/user/info/?fields=open_id,display_name,avatar_url", ct);

    public async Task<PostResult> PostVideoAsync(PostRequest request, SocialAccount account, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(account.AccessToken))
                return Fail("Account has no TikTok token; this platform is wired for real posting and cannot simulate.");

            var video = await DownloadVideoAsync(request.VideoUrl, ct);
            var privacy = Get("PrivacyLevel") ?? "SELF_ONLY";

            var init = await BearerPostJsonAsync(
                "https://open.tiktokapis.com/v2/post/publish/video/init/",
                account.AccessToken,
                JsonOf(new
                {
                    post_info = new
                    {
                        title = Truncate($"{request.Title}\n{request.Caption}".Trim(), 2200),
                        privacy_level = privacy,
                        disable_duet = false,
                        disable_comment = false,
                        disable_stitch = false
                    },
                    source_info = new
                    {
                        source = "FILE_UPLOAD",
                        video_size = video.Length,
                        chunk_size = video.Length,
                        total_chunk_count = 1
                    }
                }), ct);

            var r0 = Root(init);
            string? publishId = null;
            string? uploadUrl = null;
            if (r0 != null && r0.Value.TryGetProperty("data", out var d0))
            {
                if (d0.TryGetProperty("publish_id", out var pid)) publishId = pid.GetString();
                if (d0.TryGetProperty("upload_url", out var ur)) uploadUrl = ur.GetString();
            }
            if (string.IsNullOrEmpty(publishId) || string.IsNullOrEmpty(uploadUrl))
                return Fail("TikTok init did not return a publish_id/upload_url.");

            var put = new HttpRequestMessage(HttpMethod.Put, uploadUrl)
            {
                Content = new ByteArrayContent(video)
            };
            put.Content.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
            using (var up = await Http.SendAsync(put, ct))
            {
                var upBody = await up.Content.ReadAsStringAsync(ct);
                if (!up.IsSuccessStatusCode)
                    return Fail($"{ExtractError(upBody)} (HTTP {(int)up.StatusCode})");
            }

            for (var i = 0; i < 20; i++)
            {
                if (ct.IsCancellationRequested) break;
                await Task.Delay(3000, ct);
                var statusBody = await BearerGetAsync(
                    $"https://open.tiktokapis.com/v2/post/publish/status/?publish_id={Uri.EscapeDataString(publishId)}",
                    account.AccessToken, ct);
                var r = Root(statusBody);
                string? st = null;
                string? videoId = null;
                if (r != null && r.Value.TryGetProperty("data", out var dd))
                {
                    if (dd.TryGetProperty("status", out var stp)) st = stp.GetString();
                    if (dd.TryGetProperty("video_id", out var vd)) videoId = vd.GetString();
                }
                if (st == "SUCCESS" || st == "SEND_TO_USER_INBOX")
                {
                    if (string.IsNullOrEmpty(videoId)) videoId = publishId;
                    Logger.LogInformation("Posted TikTok video {PublishId} for {Account} (privacy {Privacy})", publishId, account.AccountHandle, privacy);
                    return new PostResult { Success = true, PostUrl = $"https://www.tiktok.com/video/{videoId}" };
                }
                if (st is "FAILED" or "FAILED_DUE_TO_SIGNATURE_INVALID")
                    return Fail($"TikTok processing failed: {st}");
            }
            return Fail("Timed out waiting for TikTok processing.");
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "TikTok posting failed");
            return Fail(ex);
        }
    }

    private static string Truncate(string s, int max)
    {
        s = (s ?? string.Empty).Trim();
        return s.Length <= max ? s : s[..max];
    }
}