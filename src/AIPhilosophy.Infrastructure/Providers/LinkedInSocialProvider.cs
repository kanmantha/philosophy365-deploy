using System.Net.Http.Headers;
using System.Text;
using AIPhilosophy.Core.Domain;
using AIPhilosophy.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AIPhilosophy.Infrastructure.Providers;

/// <summary>Real LinkedIn provider. registerUpload a digital media asset, binary PUT, then create a UGC video post.</summary>
public class LinkedInSocialProvider : RealSocialProviderBase, ISocialProvider
{
    public LinkedInSocialProvider(IConfiguration config, HttpClient http, ILogger<LinkedInSocialProvider> logger)
        : base(config, http, logger, "LinkedIn")
    {
    }

    public SocialPlatform Platform => SocialPlatform.LinkedIn;

    public async Task ValidateAccountAsync(SocialAccount account, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(account.AccessToken))
            throw new InvalidOperationException("No token on the linked account. Need a LinkedIn token with w_mem_ship and l_ugc_post.");
        var info = await ValidateBearerTokenAsync(account.AccessToken, "https://api.linkedin.com/v2/userinfo", ct);
        var r = Root(info);
        var sub = r?.TryGetProperty("sub", out var s) == true ? s.GetString() : null;
        if (!string.IsNullOrEmpty(sub))
            account.ProfileJson = JsonOf(new { person_urn = $"urn:li:person:{sub}" });
        account.LastVerifiedAt = DateTime.UtcNow;
    }

    public async Task<PostResult> PostVideoAsync(PostRequest request, SocialAccount account, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(account.AccessToken))
                return Fail("Account has no LinkedIn token; real posting cannot simulate.");

            var personUrn = account.ProfileJson is not null && JsonDoc(account.ProfileJson)?.TryGetProperty("person_urn", out var pu) == true
                ? pu.GetString()
                : Get("PersonUrn");
            if (string.IsNullOrWhiteSpace(personUrn))
                return Fail("LinkedIn person urn not resolved. Re-validate the linked account or set Social:LinkedIn:PersonUrn.");

            var video = await DownloadVideoAsync(request.VideoUrl, ct);

            var reg = await BearerPostJsonAsync(
                "https://api.linkedin.com/v2/assets?action=registerUpload",
                account.AccessToken,
                JsonOf(new
                {
                    registerUploadRequest = new
                    {
                        recipes = new[] { "urn:li:digitalmediaRecipe:feedshare-video" },
                        owner = personUrn,
                        serviceRelationships = new[]
                        {
                            new { relationshipType = "OWNER", identifier = "urn:li:userGeneratedContent" }
                        }
                    }
                }), ct);

            var rr = Root(reg);
            if (rr?.TryGetProperty("value", out var v) != true) return Fail("LinkedIn registerUpload returned no value.");
            var uploadUrl = v.GetProperty("uploadMechanism").GetProperty("com.linkedin.digitalmedia.uploading.MediaUploadHttpRequest").GetProperty("uploadUrl").GetString();
            var asset = v.GetProperty("video").GetProperty("asset").GetString();
            if (string.IsNullOrEmpty(uploadUrl) || string.IsNullOrEmpty(asset))
                return Fail("LinkedIn registerUpload returned no upload URL or asset urn.");

            using (var up = new HttpRequestMessage(HttpMethod.Put, uploadUrl) { Content = new ByteArrayContent(video) })
            using (var upResp = await Http.SendAsync(up, ct))
            {
                if (!upResp.IsSuccessStatusCode)
                {
                    var upBody = await upResp.Content.ReadAsStringAsync(ct);
                    return Fail($"{ExtractError(upBody)} (HTTP {(int)upResp.StatusCode})");
                }
            }

            var ugc = await BearerPostJsonAsync(
                "https://api.linkedin.com/v2/ugcPosts",
                account.AccessToken,
                JsonOf(new
                {
                    author = personUrn,
                    lifecycleState = "PUBLISHED",
                    specificContent = new
                    {
                        @com_LinkedIn_ugc_ShareContent = new
                        {
                            shareCommentary = new { text = Truncate($"{request.Title}\n\n{request.Caption}".Trim(), 2900) },
                            shareMediaCategory = "VIDEO",
                            media = new[]
                            {
                                new { status = "READY", description = new { text = Truncate(request.Title, 200) }, media = asset }
                            }
                        }
                    },
                    visibility = new { @com_LinkedIn_ugc_MemberNetworkVisibility = "PUBLIC" }
                }), ct);

            var ur = Root(ugc);
            var postId = ur?.TryGetProperty("id", out var id) == true ? id.GetString() : null;
            if (string.IsNullOrEmpty(postId)) return Fail("LinkedIn did not return a post id.");

            Logger.LogInformation("Posted LinkedIn video {PostId} for {Account}", postId, account.AccountHandle);
            return new PostResult { Success = true, PostUrl = $"https://www.linkedin.com/feed/update/{postId}" };
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "LinkedIn posting failed");
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