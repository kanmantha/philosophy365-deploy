using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AIPhilosophy.Core.Domain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AIPhilosophy.Infrastructure.Providers;

/// <summary>Token pair returned by a platform's OAuth token exchange.</summary>
public record OAuthTokenResult(string AccessToken, string? RefreshToken, string? TokenSecret, long? ExpiresInSeconds);

/// <summary>Authorize URL plus the PKCE verifier (when the flow uses Proof Key for Code Exchange).</summary>
public record OAuthAuthorizeResult(string Url, string? CodeVerifier);

/// <summary>
/// OAuth "Continue with &lt;Platform&gt;" flow. The API builds the platform authorize URL,
/// then exchanges the returned authorization code for tokens in the callback endpoint.
/// </summary>
public interface ISocialOAuth
{
    SocialPlatform Platform { get; }
    bool IsConfigured { get; }
    OAuthAuthorizeResult BuildAuthorizeUrl(string state, string redirectUri);
    Task<OAuthTokenResult> ExchangeAsync(string code, string redirectUri, string? codeVerifier, CancellationToken ct);
}

public abstract class SocialOAuthBase
{
    private readonly string _section;

    protected SocialOAuthBase(IConfiguration config, HttpClient http, ILogger logger, string section)
    {
        Config = config;
        Http = http;
        Logger = logger;
        _section = section;
        Http.Timeout = TimeSpan.FromSeconds(60);
    }

    protected readonly IConfiguration Config;
    protected readonly HttpClient Http;
    protected readonly ILogger Logger;

    protected string? Cfg(string key) => Config[$"Social:{_section}:{key}"];

    protected string ClientId
    {
        get
        {
            var primary = Cfg("ClientId");
            return string.IsNullOrWhiteSpace(primary) ? (Cfg("ClientKey") ?? string.Empty) : primary!;
        }
    }

    protected string ClientSecret => Cfg("ClientSecret") ?? string.Empty;

    protected bool HasKeys => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);

    protected static string NewState()
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    protected static (string Verifier, string Challenge) Pkce()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        var verifier = Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        var challenge = Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return (verifier, challenge);
    }

    protected static string BuildQuery(Dictionary<string, string> pairs)
        => "?" + string.Join("&", pairs.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));

    protected async Task<OAuthTokenResult> ParseTokenAsync(HttpResponseMessage resp, string body)
    {
        string? access = null, refresh = null, secret = null;
        long? expires = null;
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                var r = doc.RootElement;
                if (r.TryGetProperty("access_token", out var at)) access = at.GetString();
                if (r.TryGetProperty("refresh_token", out var rt)) refresh = rt.GetString();
                if (r.TryGetProperty("oauth_token_secret", out var os)) secret = os.GetString();
                if (r.TryGetProperty("token_secret", out var ts)) secret = ts.GetString();
                if (r.TryGetProperty("expires_in", out var ei) && ei.ValueKind == JsonValueKind.Number) expires = ei.GetInt64();
            }
            catch
            {
                // not JSON - error text handled below
            }
        }

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Token exchange failed (HTTP {(int)resp.StatusCode}): {RealSocialProviderBase.ExtractError(body)}");
        if (string.IsNullOrWhiteSpace(access))
            throw new InvalidOperationException($"Platform did not return an access token: {RealSocialProviderBase.ExtractError(body)}");

        return new OAuthTokenResult(access!, refresh, secret, expires);
    }
}

/// <summary>Google / YouTube OAuth 2.0 for the youtube.upload scope (authorization code + PKCE).</summary>
public class GoogleSocialOAuth : SocialOAuthBase, ISocialOAuth
{
    public GoogleSocialOAuth(IConfiguration config, HttpClient http, ILogger<GoogleSocialOAuth> logger)
        : base(config, http, logger, "YouTubeShorts")
    {
    }

    public SocialPlatform Platform => SocialPlatform.YouTubeShorts;

    public bool IsConfigured => HasKeys;

    public OAuthAuthorizeResult BuildAuthorizeUrl(string state, string redirectUri)
    {
        var (verifier, challenge) = Pkce();
        var query = BuildQuery(new Dictionary<string, string>
        {
            ["client_id"] = ClientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = "openid https://www.googleapis.com/auth/youtube.upload",
            ["access_type"] = "offline",
            ["prompt"] = "consent",
            ["include_granted_scopes"] = "true",
            ["state"] = state,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256"
        });
        return new OAuthAuthorizeResult($"https://accounts.google.com/o/oauth2/v2/auth{query}", verifier);
    }

    public async Task<OAuthTokenResult> ExchangeAsync(string code, string redirectUri, string? codeVerifier, CancellationToken ct)
    {
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = ClientId,
            ["client_secret"] = ClientSecret,
            ["redirect_uri"] = redirectUri,
            ["grant_type"] = "authorization_code",
            ["code_verifier"] = codeVerifier ?? string.Empty
        });
        using var resp = await Http.PostAsync("https://oauth2.googleapis.com/token", form, ct);
        return await ParseTokenAsync(resp, await resp.Content.ReadAsStringAsync(ct));
    }
}

/// <summary>Shared Meta Graph API OAuth (Instagram + Facebook) with a long-lived token upgrade.</summary>
public abstract class MetaSocialOAuth : SocialOAuthBase, ISocialOAuth
{
    private readonly string _ver;

    protected MetaSocialOAuth(IConfiguration config, HttpClient http, ILogger logger, string section)
        : base(config, http, logger, section)
    {
        _ver = Cfg("GraphApiVersion") ?? "v22.0";
    }

    public abstract SocialPlatform Platform { get; }
    public abstract string Scope { get; }

    public bool IsConfigured => HasKeys;

    public OAuthAuthorizeResult BuildAuthorizeUrl(string state, string redirectUri)
    {
        var query = BuildQuery(new Dictionary<string, string>
        {
            ["client_id"] = ClientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = Scope,
            ["state"] = state
        });
        return new OAuthAuthorizeResult($"https://www.facebook.com/{_ver}/dialog/oauth{query}", null);
    }

    public async Task<OAuthTokenResult> ExchangeAsync(string code, string redirectUri, string? codeVerifier, CancellationToken ct)
    {
        var shortTokenUrl = $"https://graph.facebook.com/{_ver}/oauth/access_token{BuildQuery(new Dictionary<string, string>
        {
            ["client_id"] = ClientId,
            ["client_secret"] = ClientSecret,
            ["redirect_uri"] = redirectUri,
            ["code"] = code,
            ["response_format"] = "json"
        })}";
        var shortBody = await Http.GetStringAsync(shortTokenUrl, ct);
        var shortToken = ReadAccessToken(shortBody);

        // Upgrade the short-lived token (valid ~1h) to a long-lived one (valid ~60 days) for posting.
        var longUrl = $"https://graph.facebook.com/{_ver}/oauth/access_token{BuildQuery(new Dictionary<string, string>
        {
            ["grant_type"] = "fb_exchange_token",
            ["client_id"] = ClientId,
            ["client_secret"] = ClientSecret,
            ["fb_exchange_token"] = shortToken
        })}";
        var longBody = await Http.GetStringAsync(longUrl, ct);

        long? expires = null;
        try
        {
            using var doc = JsonDocument.Parse(longBody);
            var r = doc.RootElement;
            if (r.TryGetProperty("expires_in", out var ei) && ei.ValueKind == JsonValueKind.Number) expires = ei.GetInt64();
        }
        catch { /* ignore */ }

        return new OAuthTokenResult(ReadAccessToken(longBody), null, null, expires);
    }

    private static string ReadAccessToken(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("access_token", out var at))
                return at.GetString() ?? throw new InvalidOperationException($"Meta did not return an access token: {RealSocialProviderBase.ExtractError(body)}");
        }
        catch
        {
            throw new InvalidOperationException($"Meta token exchange failed: {RealSocialProviderBase.ExtractError(body)}");
        }
        throw new InvalidOperationException($"Meta did not return an access token: {RealSocialProviderBase.ExtractError(body)}");
    }
}

/// <summary>Instagram Business OAuth (business_management + instagram_business_content_publish).</summary>
public class InstagramSocialOAuth : MetaSocialOAuth
{
    public InstagramSocialOAuth(IConfiguration config, HttpClient http, ILogger<InstagramSocialOAuth> logger)
        : base(config, http, logger, "InstagramReels")
    {
    }

    public override SocialPlatform Platform => SocialPlatform.InstagramReels;

    public override string Scope => "instagram_business_basic,instagram_business_content_publish,business_management,pages_show_list,pages_manage_posts";
}

/// <summary>Facebook OAuth (pages_manage_posts + pages_show_list).</summary>
public class FacebookSocialOAuth : MetaSocialOAuth
{
    public FacebookSocialOAuth(IConfiguration config, HttpClient http, ILogger<FacebookSocialOAuth> logger)
        : base(config, http, logger, "FacebookReels")
    {
    }

    public override SocialPlatform Platform => SocialPlatform.FacebookReels;

    public override string Scope => "pages_manage_posts,pages_show_list";
}

/// <summary>TikTok Content Posting API OAuth (user.info.basic + video.publish).</summary>
public class TikTokSocialOAuth : SocialOAuthBase, ISocialOAuth
{
    public TikTokSocialOAuth(IConfiguration config, HttpClient http, ILogger<TikTokSocialOAuth> logger)
        : base(config, http, logger, "TikTok")
    {
    }

    public SocialPlatform Platform => SocialPlatform.TikTok;

    public bool IsConfigured => HasKeys;

    public OAuthAuthorizeResult BuildAuthorizeUrl(string state, string redirectUri)
    {
        var query = BuildQuery(new Dictionary<string, string>
        {
            ["client_key"] = ClientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = "user.info.basic,video.publish",
            ["state"] = state
        });
        return new OAuthAuthorizeResult($"https://www.tiktok.com/v2/auth/authorize/{query}", null);
    }

    public async Task<OAuthTokenResult> ExchangeAsync(string code, string redirectUri, string? codeVerifier, CancellationToken ct)
    {
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_key"] = ClientId,
            ["client_secret"] = ClientSecret,
            ["code"] = code,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = redirectUri
        });
        using var resp = await Http.PostAsync("https://open.tiktokapis.com/v2/oauth/token/", form, ct);
        return await ParseTokenAsync(resp, await resp.Content.ReadAsStringAsync(ct));
    }
}

/// <summary>LinkedIn OAuth (openid + profile + w_mem_ship + l_ugc_post).</summary>
public class LinkedInSocialOAuth : SocialOAuthBase, ISocialOAuth
{
    public LinkedInSocialOAuth(IConfiguration config, HttpClient http, ILogger<LinkedInSocialOAuth> logger)
        : base(config, http, logger, "LinkedIn")
    {
    }

    public SocialPlatform Platform => SocialPlatform.LinkedIn;

    public bool IsConfigured => HasKeys;

    public OAuthAuthorizeResult BuildAuthorizeUrl(string state, string redirectUri)
    {
        var query = BuildQuery(new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = ClientId,
            ["redirect_uri"] = redirectUri,
            ["scope"] = "openid profile w_mem_ship l_ugc_post",
            ["state"] = state
        });
        return new OAuthAuthorizeResult($"https://www.linkedin.com/oauth/v2/authorization{query}", null);
    }

    public async Task<OAuthTokenResult> ExchangeAsync(string code, string redirectUri, string? codeVerifier, CancellationToken ct)
    {
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = ClientId,
            ["client_secret"] = ClientSecret
        });
        using var resp = await Http.PostAsync("https://www.linkedin.com/oauth/v2/accessToken", form, ct);
        return await ParseTokenAsync(resp, await resp.Content.ReadAsStringAsync(ct));
    }
}