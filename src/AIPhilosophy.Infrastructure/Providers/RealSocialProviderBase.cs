using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AIPhilosophy.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AIPhilosophy.Infrastructure.Providers;

/// <summary>
/// Shared plumbing for real platform providers: downloads the video bytes from the local API,
/// extracts API error messages, executes bearer-authenticated requests and signs OAuth1 (X/Twitter).
/// Provider subclasses only implement the platform-specific upload choreography.
/// </summary>
public abstract class RealSocialProviderBase
{
    public const int MaxVideoBytes = 256 * 1024 * 1024;
    public const int DefaultTimeoutSeconds = 60;

    protected readonly IConfiguration Config;
    protected readonly HttpClient Http;
    protected readonly ILogger Logger;
    private readonly string _section;

    protected RealSocialProviderBase(IConfiguration config, HttpClient http, ILogger logger, string section)
    {
        Config = config;
        Http = http;
        Logger = logger;
        _section = section;
        Http.Timeout = TimeSpan.FromSeconds(DefaultTimeoutSeconds);
    }

    /// <summary>Whether app credentials for this platform are present in config (OAuth possibility).</summary>
    public bool IsConfigured
    {
        get
        {
            var keys = new[]
            {
                $"Social:{_section}:ClientId",
                $"Social:{_section}:ClientSecret",
                $"Social:{_section}:ClientKey",
                $"Social:{_section}:ApiKey",
                $"Social:{_section}:ApiSecret",
                $"Social:{_section}:BearerToken"
            };
            return keys.Any(k => !string.IsNullOrWhiteSpace(Config[k]));
        }
    }

    protected string? Get(string key) => Config[$"Social:{_section}:{key}"];

    protected static string SectionNoSecret(string section)
        => section switch
        {
            "XTwitter" => "Consumer key/secret + a user token with video upload scopes",
            "YouTubeShorts" => "Client id/secret + a token with youtube.upload",
            "TikTok" => "Client key/secret + a token with video.publish",
            "InstagramReels" => "Meta app id/secret + a token with instagram_business_content_publish",
            "LinkedIn" => "Client id/secret + a token with w_mem_ship and l_ugc_post",
            "FacebookReels" => "Meta app id/secret + a token with pages_manage_posts",
            _ => "real credentials"
        };

    protected static PostResult Fail(string message) => new() { Success = false, Error = message };

    protected static PostResult Fail(Exception ex) => new() { Success = false, Error = ex.Message };

    public static string ExtractError(string? body)
    {
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                var candidate = FindErrorField(root);
                if (!string.IsNullOrEmpty(candidate))
                    return RootClean(candidate);
            }
            catch
            {
                // not json — use raw text below
            }
        }
        var pretty = Regex.Replace(body ?? string.Empty, @"\s+", " ").Trim();
        return pretty.Length > 320 ? pretty[..320] : pretty;
    }

    private static string? FindErrorField(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;
        if (root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(m.GetString()))
            return m.GetString();
        if (root.TryGetProperty("error", out var e)) return RecurseError(e);
        if (root.TryGetProperty("errors", out var es)) return RecurseError(es);
        if (root.TryGetProperty("status_code", out var sc) && sc.ValueKind == JsonValueKind.Number)
        {
            var d = root.TryGetProperty("data", out var dd) ? dd.GetRawText() : string.Empty;
            return $"{(int)sc.GetInt64()} {RootClean(d)}".Trim();
        }
        return null;
    }

    private static string? RecurseError(JsonElement el)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var p in new[] { "message", "msg", "description", "reason" })
                    if (el.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(v.GetString()))
                        return v.GetString();
                if (el.TryGetProperty("errors", out var inner))
                    return RecurseError(inner);
                return null;
            case JsonValueKind.Array:
                foreach (var item in el.EnumerateArray().Take(3))
                {
                    var r = RecurseError(item);
                    if (!string.IsNullOrEmpty(r)) return r;
                }
                return null;
            default:
                return null;
        }
    }

    private static string RootClean(string s)
        => Regex.Replace(Regex.Replace(s ?? string.Empty, @"\s+", " "), @"[""\\]", string.Empty).Trim();

    /// <summary>Downloads the generated video (from the local API) into memory for upload.</summary>
    protected async Task<byte[]> DownloadVideoAsync(string videoUrl, CancellationToken ct)
    {
        using var resp = await Http.GetAsync(videoUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength ?? -1;
        if (total > MaxVideoBytes)
            throw new InvalidOperationException($"Video is {total:N0} bytes, above the {MaxVideoBytes:N0} upload limit.");
        var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
        if (bytes.Length == 0)
            throw new InvalidOperationException("Downloaded video is empty.");
        return bytes;
    }

    /// <summary>Sends a bearer request and returns the response body, throwing with the platform's own message on failure.</summary>
    protected async Task<string> BearerSendAsync(HttpMethod method, string url, string? token, HttpContent? content, string? contentType, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(method, url);
        if (!string.IsNullOrWhiteSpace(token))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (content != null)
        {
            req.Content = content;
            if (!string.IsNullOrWhiteSpace(contentType))
                req.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        }
        using var resp = await Http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"{ExtractError(body)} (HTTP {(int)resp.StatusCode})");
        return body;
    }

    protected Task<string> BearerGetAsync(string url, string? token, CancellationToken ct)
        => BearerSendAsync(HttpMethod.Get, url, token, null, null, ct);

    protected Task<string> BearerPostJsonAsync(string url, string? token, string json, CancellationToken ct)
        => BearerSendAsync(HttpMethod.Post, url, token, new StringContent(json, Encoding.UTF8, "application/json"), "application/json; charset=utf-8", ct);

    protected Task<string> BearerPutAsync(string url, string? token, byte[] bytes, string contentType, CancellationToken ct)
        => BearerSendAsync(HttpMethod.Put, url, token, new ByteArrayContent(bytes), contentType, ct);

    /// <summary>Validates a bearer token against a simple GET endpoint; throws with the platform's own HTTP error. Returns response body.</summary>
    protected async Task<string> ValidateBearerTokenAsync(string token, string validateUrl, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("No access token on the linked account. Link the account with the real token for this platform.");
        return await BearerGetAsync(validateUrl, token, ct);
    }

    /// <summary>Short JSON string helper to stash profile data on the account.</summary>
    protected static string JsonOf(object o)
        => JsonSerializer.Serialize(o);

    /// <summary>Reads <see cref="JsonElement"/>-friendly extra parsing of a tiny JSON body (for actor ids etc.).</summary>
    protected static JsonElement? Root(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement;
        }
        catch
        {
            return null;
        }
    }

    public static string SignOAuth1(
        string httpMethod,
        string url,
        IReadOnlyDictionary<string, string> formParams,
        string consumerKey,
        string consumerSecret,
        string token,
        string tokenSecret)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var oauth = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["oauth_consumer_key"] = consumerKey,
            ["oauth_nonce"] = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant(),
            ["oauth_signature_method"] = "HMAC-SHA1",
            ["oauth_timestamp"] = timestamp,
            ["oauth_token"] = token,
            ["oauth_version"] = "1.0"
        };

        var all = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var kv in oauth) all[kv.Key] = kv.Value;
        foreach (var kv in formParams) all[kv.Key] = kv.Value;

        var parameterString = string.Join("&", all.Select(kv => $"{PurlEncode(kv.Key)}={PurlEncode(kv.Value)}"));
        var baseString = $"{httpMethod.ToUpperInvariant()}&{PurlEncode(url)}&{PurlEncode(parameterString)}";
        var signingKey = $"{PurlEncode(consumerSecret)}&{PurlEncode(tokenSecret)}";

        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(signingKey));
        var signature = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(baseString)));

        var headerParts = new List<string>
        {
            $"oauth_consumer_key=\"{PurlEncode(consumerKey)}\"",
            $"oauth_nonce=\"{PurlEncode(oauth["oauth_nonce"])}\"",
            "oauth_signature_method=\"HMAC-SHA1\"",
            $"oauth_timestamp=\"{timestamp}\"",
            $"oauth_token=\"{PurlEncode(token)}\"",
            $"oauth_version=\"1.0\"",
            $"oauth_signature=\"{PurlEncode(signature)}\""
        };
        return "OAuth " + string.Join(", ", headerParts);
    }

    private static string PurlEncode(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        const string unreserved = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_.~";
        var sb = new StringBuilder();
        foreach (var b in Encoding.UTF8.GetBytes(value))
        {
            var c = (char)b;
            if (unreserved.IndexOf(c) >= 0) sb.Append(c);
            else sb.Append('%').Append(b.ToString("X2"));
        }
        return sb.ToString();
    }
}