using System.Net.Http.Headers;
using AIPhilosophy.Core.Domain;
using AIPhilosophy.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AIPhilosophy.Infrastructure.Providers;

/// <summary>Real Facebook (Reels/video) provider via Meta Graph API. Binary raw-video POST to /{actor}/videos.</summary>
public class FacebookSocialProvider : RealSocialProviderBase, ISocialProvider
{
    private readonly string _ver;

    public FacebookSocialProvider(IConfiguration config, HttpClient http, ILogger<FacebookSocialProvider> logger)
        : base(config, http, logger, "FacebookReels")
    {
        _ver = Get("GraphApiVersion") ?? "v22.0";
    }

    public SocialPlatform Platform => SocialPlatform.FacebookReels;

    public async Task ValidateAccountAsync(SocialAccount account, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(account.AccessToken))
            throw new InvalidOperationException("No token on the linked account. Need a Meta token with pages_manage_posts.");
        var me = await ValidateBearerTokenAsync(account.AccessToken, $"https://graph.facebook.com/{_ver}/me?fields=id,name", ct);
        var meR = Root(me);
        var userId = meR?.TryGetProperty("id", out var uid) == true ? uid.GetString() : null;

        string? pageId = Get("PageId");
        string? pageName = null;
        try
        {
            var pages = await BearerGetAsync($"https://graph.facebook.com/{_ver}/me/accounts?fields=id,name", account.AccessToken, ct);
            var r = Root(pages);
            if (r?.TryGetProperty("data", out var arr) == true && arr.ValueKind == System.Text.Json.JsonValueKind.Array)
                foreach (var p in arr.EnumerateArray())
                    if (p.TryGetProperty("id", out var pv) && !string.IsNullOrWhiteSpace(pv.GetString()))
                    {
                        if (string.IsNullOrEmpty(pageId)) pageId = pv.GetString();
                        if (p.TryGetProperty("name", out var pn)) pageName = pn.GetString();
                        break;
                    }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Token looks valid but the managed pages could not be listed ({ex.Message}). Grant pages_show_list + pages_manage_posts.");
        }

        if (string.IsNullOrWhiteSpace(pageId))
            throw new InvalidOperationException("Token is valid but this Facebook user manages no pages. Manage a page and re-check the token scopes (pages_manage_posts).");

        account.ProfileJson = JsonOf(new { fb_user_id = userId, page_id = pageId, page_name = pageName });
        account.LastVerifiedAt = DateTime.UtcNow;
    }

    public async Task<PostResult> PostVideoAsync(PostRequest request, SocialAccount account, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(account.AccessToken))
                return Fail("Account has no Facebook token; real posting cannot simulate.");

            var pageId = account.ProfileJson is not null && JsonDoc(account.ProfileJson)?.TryGetProperty("page_id", out var pid) == true && !string.IsNullOrWhiteSpace(pid.GetString())
                ? pid.GetString()
                : Get("PageId");
            if (string.IsNullOrWhiteSpace(pageId))
                return Fail("Facebook page id not resolved. Re-validate the linked account or set Social:FacebookReels:PageId.");

            var video = await DownloadVideoAsync(request.VideoUrl, ct);
            var url = $"https://graph.facebook.com/{_ver}/{pageId}/videos?description={Uri.EscapeDataString($"{request.Title}\n\n{request.Caption}".Trim())}&title={Uri.EscapeDataString(Truncate(request.Title, 100))}&access_token={Uri.EscapeDataString(account.AccessToken)}";

            var post = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new ByteArrayContent(video)
            };
            post.Content.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");

            using var resp = await Http.SendAsync(post, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                return Fail($"{ExtractError(body)} (HTTP {(int)resp.StatusCode})");

            var videoId = Root(body)?.TryGetProperty("id", out var vid) == true ? vid.GetString() : null;
            if (string.IsNullOrEmpty(videoId)) return Fail("Facebook did not return a video id.");

            Logger.LogInformation("Posted Facebook video {VideoId} for {Account}", videoId, account.AccountHandle);
            return new PostResult { Success = true, PostUrl = $"https://www.facebook.com/{videoId}" };
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Facebook posting failed");
            return Fail(ex);
        }
    }

    private static System.Text.Json.JsonElement? JsonDoc(string json)
    {
        try
        {
            using var d = System.Text.Json.JsonDocument.Parse(json);
            return d.RootElement;
        }
        catch { return null; }
    }

    private static string Truncate(string s, int max)
    {
        s = (s ?? string.Empty).Trim();
        return s.Length <= max ? s : s[..max];
    }
}