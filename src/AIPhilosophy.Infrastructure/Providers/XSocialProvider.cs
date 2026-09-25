using System.Net.Http.Headers;
using System.Text;
using AIPhilosophy.Core.Domain;
using AIPhilosophy.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AIPhilosophy.Infrastructure.Providers;

/// <summary>Real X (Twitter) provider. Uploads media via OAuth1 media/upload (INIT/APPEND/FINALIZE) then creates a v2 tweet.</summary>
public class XSocialProvider : RealSocialProviderBase, ISocialProvider
{
    public XSocialProvider(IConfiguration config, HttpClient http, ILogger<XSocialProvider> logger)
        : base(config, http, logger, "XTwitter")
    {
    }

    public SocialPlatform Platform => SocialPlatform.XTwitter;

    public Task ValidateAccountAsync(SocialAccount account, CancellationToken ct)
        => ValidateBearerTokenAsync(account.AccessToken, "https://api.x.com/2/users/me", ct);

    public async Task<PostResult> PostVideoAsync(PostRequest request, SocialAccount account, CancellationToken ct)
    {
        try
        {
            var apiKey = Get("ApiKey");
            var apiSecret = Get("ApiSecret");
            if (string.IsNullOrWhiteSpace(account.AccessToken) || string.IsNullOrWhiteSpace(account.TokenSecret))
                return Fail("X requires an OAuth1 user token AND token secret on the linked account (real posting cannot simulate).");
            if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(apiSecret))
                return Fail("Set Social:XTwitter:ApiKey / ApiSecret in config for real X posting.");

            var video = await DownloadVideoAsync(request.VideoUrl, ct);
            var mediaId = await UploadMediaAsync(video, account, apiKey!, apiSecret!, ct);
            if (string.IsNullOrEmpty(mediaId))
                return Fail("X media upload did not return a media id.");

            var tweetBody = await BearerPostJsonAsync(
                "https://api.x.com/2/tweets",
                account.AccessToken,
                JsonOf(new
                {
                    text = Truncate($"{request.Title}\n\n{request.Caption}".Trim(), 279),
                    media = new { media_ids = new[] { mediaId } }
                }), ct);

            var r = Root(tweetBody);
            var tweetId = r?.TryGetProperty("data", out var d) == true && d.TryGetProperty("id", out var id) ? id.GetString() : null;
            if (string.IsNullOrEmpty(tweetId))
                return Fail("X accepted the tweet but returned no id.");

            Logger.LogInformation("Posted X video tweet {TweetId} for {Account}", tweetId, account.AccountHandle);
            return new PostResult { Success = true, PostUrl = $"https://x.com/i/status/{tweetId}" };
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "X posting failed");
            return Fail(ex);
        }
    }

    private async Task<string> UploadMediaAsync(byte[] video, SocialAccount account, string apiKey, string apiSecret, CancellationToken ct)
    {
        var mediaUrl = "https://upload.twitter.com/1.1/media/upload.json";
        var total = video.Length.ToString();

        using var initForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["commands"] = "INIT",
            ["media_type"] = "video/mp4",
            ["total_bytes"] = total,
            ["media_category"] = "tweet_video"
        });
        var initBody = await SendOAuth1Async(HttpMethod.Post, mediaUrl, initForm, account, apiKey, apiSecret, ct);
        var ir = Root(initBody);
        string? mediaId = null;
        if (ir != null)
        {
            if (ir.Value.TryGetProperty("media_id_string", out var ms)) mediaId = ms.GetString();
            else if (ir.Value.TryGetProperty("data", out var data) && data.TryGetProperty("media_id_string", out var ms2)) mediaId = ms2.GetString();
        }
        if (string.IsNullOrEmpty(mediaId))
            throw new InvalidOperationException($"X media INIT failed: {ExtractError(initBody)}");

        const int chunkSize = 4 * 1024 * 1024;
        var index = 0;
        for (var offset = 0; offset < video.Length; offset += chunkSize, index++)
        {
            var chunk = video[offset..Math.Min(offset + chunkSize, video.Length)];
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent("APPEND"), "commands");
            form.Add(new StringContent(mediaId), "media_id");
            form.Add(new StringContent(index.ToString()), "segment_index");
            form.Add(new ByteArrayContent(chunk), "media", "video.mp4");
            await SendOAuth1Async(HttpMethod.Post, mediaUrl, form, account, apiKey, apiSecret, ct);
        }

        using var finalForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["commands"] = "FINALIZE",
            ["media_id"] = mediaId
        });
        var finalBody = await SendOAuth1Async(HttpMethod.Post, mediaUrl, finalForm, account, apiKey, apiSecret, ct);

        // poll processing state
        for (var i = 0; i < 15; i++)
        {
            if (ct.IsCancellationRequested) break;
            using var statusForm = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["commands"] = "STATUS",
                ["media_id"] = mediaId
            });
            var statusBody = await SendOAuth1Async(HttpMethod.Post, mediaUrl, statusForm, account, apiKey, apiSecret, ct);
            var r = Root(statusBody);
            var state = r?.TryGetProperty("processing_info", out var pi) == true && pi.TryGetProperty("state", out var st)
                ? st.GetString()
                : "succeeded";
            if (state == "succeeded")
                return mediaId;
            if (state is "failed" or "failed_processing")
                throw new InvalidOperationException($"X media processing failed: {ExtractError(statusBody)}");
            await Task.Delay(2000, ct);
        }
        throw new InvalidOperationException("Timed out waiting for X media processing.");
    }

    private async Task<string> SendOAuth1Async(HttpMethod method, string url, HttpContent content, SocialAccount account, string apiKey, string apiSecret, CancellationToken ct)
    {
        var oauth = SignOAuth1(method.ToString(), url,
            ParseForm(content),
            apiKey, apiSecret, account.AccessToken ?? string.Empty, account.TokenSecret ?? string.Empty);

        using var req = new HttpRequestMessage(method, url) { Content = content };
        req.Headers.TryAddWithoutValidation("Authorization", oauth);
        using var resp = await Http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"{ExtractError(body)} (HTTP {(int)resp.StatusCode})");
        return body;
    }

    private static Dictionary<string, string> ParseForm(HttpContent content)
    {
        var dict = new Dictionary<string, string>();
        if (content is FormUrlEncodedContent form)
        {
            var encoded = form.ReadAsStringAsync().GetAwaiter().GetResult();
            foreach (var pair in encoded.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = pair.Split('=', 2);
                if (parts.Length == 2)
                    dict[Uri.UnescapeDataString(parts[0])] = Uri.UnescapeDataString(parts[1]);
            }
        }
        return dict;
    }

    private static string Truncate(string s, int max)
    {
        s = (s ?? string.Empty).Trim();
        return s.Length <= max ? s : s[..max];
    }
}