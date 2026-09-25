using System.Net.Http.Headers;
using AIPhilosophy.Core.Domain;
using AIPhilosophy.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AIPhilosophy.Infrastructure.Providers;

/// <summary>Real Instagram Reels provider via Meta Graph API. Binary (raw video bytes) reels container + media_publish.</summary>
public class InstagramSocialProvider : RealSocialProviderBase, ISocialProvider
{
    private readonly string _ver;

    public InstagramSocialProvider(IConfiguration config, HttpClient http, ILogger<InstagramSocialProvider> logger)
        : base(config, http, logger, "InstagramReels")
    {
        _ver = Get("GraphApiVersion") ?? "v22.0";
    }

    public SocialPlatform Platform => SocialPlatform.InstagramReels;

    public async Task ValidateAccountAsync(SocialAccount account, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(account.AccessToken))
            throw new InvalidOperationException("No token on the linked account. Need a Meta token with instagram_business_content_publish.");

        var me = await ValidateBearerTokenAsync(account.AccessToken, $"https://graph.facebook.com/{_ver}/me?fields=id,name", ct);

        string? igUserId = null;
        string? igUsername = null;
        try
        {
            var pages = await BearerGetAsync($"https://graph.facebook.com/{_ver}/me/accounts?fields=instagram_business_account%7Bid,username%7D", account.AccessToken, ct);
            var r = Root(pages);
            if (r?.TryGetProperty("data", out var arr) == true && arr.ValueKind == System.Text.Json.JsonValueKind.Array)
                foreach (var p in arr.EnumerateArray())
                    if (p.TryGetProperty("instagram_business_account", out var ig) && ig.TryGetProperty("id", out var iid) && !string.IsNullOrWhiteSpace(iid.GetString()))
                    {
                        igUserId = iid.GetString();
                        if (ig.TryGetProperty("username", out var iu)) igUsername = iu.GetString();
                        break;
                    }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Token looks valid but the linked accounts could not be listed ({ex.Message}). Grant pages_show_list + instagram_business_content_publish.");
        }

        if (string.IsNullOrWhiteSpace(igUserId))
            throw new InvalidOperationException("Token is valid but no Instagram Business account is linked to this Facebook user. Link a business account and re-check the token scopes (instagram_business_content_publish).");

        var meR = Root(me);
        var userId = meR?.TryGetProperty("id", out var uid) == true ? uid.GetString() : null;
        account.ProfileJson = JsonOf(new { fb_user_id = userId, ig_user_id = igUserId, ig_username = igUsername });
        account.LastVerifiedAt = DateTime.UtcNow;
    }

    public async Task<PostResult> PostVideoAsync(PostRequest request, SocialAccount account, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(account.AccessToken))
                return Fail("Account has no Instagram token; this platform is wired for real posting and cannot simulate.");

            var igUserId = account.ProfileJson is not null && JsonDoc(account.ProfileJson)?.TryGetProperty("ig_user_id", out var ig) == true
                ? ig.GetString()
                : Get("IgUserId");
            if (string.IsNullOrWhiteSpace(igUserId))
                return Fail("Instagram Business account id not resolved. Re-validate the linked account, or set Social:InstagramReels:IgUserId.");

            var video = await DownloadVideoAsync(request.VideoUrl, ct);
            var caption = Truncate($"{request.Title}\n\n{request.Caption}".Trim(), 2200);

            var createUrl = $"https://graph.facebook.com/{_ver}/{igUserId}/media?media_type=REELS&share_to_feed=true&caption={Uri.EscapeDataString(caption)}&access_token={Uri.EscapeDataString(account.AccessToken)}";

            var create = new HttpRequestMessage(HttpMethod.Post, createUrl)
            {
                Content = new ByteArrayContent(video)
            };
            create.Content.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");

            using var created = await Http.SendAsync(create, ct);
            var createdBody = await created.Content.ReadAsStringAsync(ct);
            if (!created.IsSuccessStatusCode)
                return Fail($"{ExtractError(createdBody)} (HTTP {(int)created.StatusCode})");

            var cr = Root(createdBody);
            var creationId = cr != null && cr.Value.TryGetProperty("id", out var cid) ? cid.GetString() : null;
            if (string.IsNullOrEmpty(creationId))
                return Fail("Instagram did not return a container id.");

            var publishUrl = $"https://graph.facebook.com/{_ver}/{igUserId}/media_publish?creation_id={Uri.EscapeDataString(creationId)}&access_token={Uri.EscapeDataString(account.AccessToken)}";

            for (var i = 0; i < 10; i++)
            {
                if (ct.IsCancellationRequested) break;
                await Task.Delay(2000, ct);
                try
                {
                    var pub = await BearerPostJsonAsync(publishUrl, null, string.Empty, ct);
                    var pr = Root(pub);
                    var mediaId = pr?.TryGetProperty("id", out var mid) == true ? mid.GetString() : null;
                    if (!string.IsNullOrEmpty(mediaId))
                    {
                        Logger.LogInformation("Posted Instagram Reel {MediaId} for {Account}", mediaId, account.AccountHandle);
                        return new PostResult { Success = true, PostUrl = $"https://www.instagram.com/reel/{mediaId}" };
                    }
                }
                catch (HttpRequestException hex)
                {
                    if (!hex.Message.Contains("(HTTP 400)"))
                        throw;
                    Logger.LogInformation("Instagram container not ready, retrying: {Msg}", hex.Message);
                }
            }
            return Fail("Timed out publishing Instagram reel container.");
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Instagram posting failed");
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