using System.Net.Http.Headers;
using System.Text;
using AIPhilosophy.Core.Domain;
using AIPhilosophy.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AIPhilosophy.Infrastructure.Providers;

/// <summary>Real YouTube provider. Validates via youtube/v3 channels and uploads with the resumable upload API.</summary>
public class YouTubeSocialProvider : RealSocialProviderBase, ISocialProvider
{
    public YouTubeSocialProvider(IConfiguration config, HttpClient http, ILogger<YouTubeSocialProvider> logger)
        : base(config, http, logger, "YouTubeShorts")
    {
    }

    public SocialPlatform Platform => SocialPlatform.YouTubeShorts;

    public Task ValidateAccountAsync(SocialAccount account, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(account.AccessToken))
            throw new InvalidOperationException("No token on the linked account. Link the account with a real Google OAuth token (scope youtube.upload).");
        return ValidateBearerTokenAsync(account.AccessToken, "https://www.googleapis.com/youtube/v3/channels?part=snippet&mine=true", ct);
    }

    public async Task<PostResult> PostVideoAsync(PostRequest request, SocialAccount account, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(account.AccessToken))
                return Fail("Account has no YouTube token; this platform is wired for real posting and cannot simulate.");

            var video = await DownloadVideoAsync(request.VideoUrl, ct);

            var snippet = new
            {
                title = Truncate(request.Title, 95),
                description = Truncate($"{request.Description}\n\n{request.Hashtags}".Trim(), 4900),
                tags = SplitTags(request.Hashtags)
            };
            var status = new { privacyStatus = "public", selfDeclaredMadeForKids = false };
            var metadata = JsonOf(new { snippet, status });

            var uploadReady = new HttpRequestMessage(HttpMethod.Post,
                "https://www.googleapis.com/upload/youtube/v3/videos?uploadType=resumable&part=snippet,status");
            uploadReady.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.AccessToken);
            uploadReady.Content = new StringContent(metadata, Encoding.UTF8, "application/json");
            uploadReady.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json; charset=UTF-8");
            uploadReady.Headers.Add("X-Upload-Content-Type", "video/mp4");
            uploadReady.Headers.Add("X-Upload-Content-Length", video.Length.ToString());

            using var ready = await Http.SendAsync(uploadReady, ct);
            var readyBody = await ready.Content.ReadAsStringAsync(ct);
            if (!ready.IsSuccessStatusCode)
                return Fail($"{ExtractError(readyBody)} (HTTP {(int)ready.StatusCode})");

            var location = ready.Headers.Location?.AbsoluteUri;
            if (string.IsNullOrEmpty(location))
                return Fail("YouTube did not return an upload session URL.");

            var upload = new HttpRequestMessage(HttpMethod.Put, location)
            {
                Content = new ByteArrayContent(video)
            };
            upload.Content.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");

            using var done = await Http.SendAsync(upload, ct);
            var doneBody = await done.Content.ReadAsStringAsync(ct);
            if (!done.IsSuccessStatusCode)
                return Fail($"{ExtractError(doneBody)} (HTTP {(int)done.StatusCode})");

            var id = Root(doneBody)?.TryGetProperty("id", out var vid) == true ? vid.GetString() : null;
            if (string.IsNullOrEmpty(id))
                return Fail("YouTube upload finished but no video id was returned.");

            Logger.LogInformation("Posted YouTube Shorts video {Id} for {Account}", id, account.AccountHandle);
            return new PostResult { Success = true, PostUrl = $"https://www.youtube.com/watch?v={id}" };
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "YouTube posting failed");
            return Fail(ex);
        }
    }

    private static string[] SplitTags(string hashtags)
        => string.IsNullOrWhiteSpace(hashtags)
            ? Array.Empty<string>()
            : hashtags.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.TrimStart('#')).Where(t => t.Length > 0).Take(20).ToArray();

    private static string Truncate(string s, int max)
    {
        s = (s ?? string.Empty).Trim();
        return s.Length <= max ? s : s[..max];
    }
}