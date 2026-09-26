using System.Collections.Concurrent;
using AIPhilosophy.Api.Models;
using AIPhilosophy.Core.Domain;
using AIPhilosophy.Infrastructure.Data;
using AIPhilosophy.Infrastructure.Providers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AIPhilosophy.Api.Controllers;

/// <summary>
/// OAuth "Continue with &lt;Platform&gt;" flow. /start returns the platform's authorize URL
/// (with a state nonce that maps back to this app's tenant); /callback exchanges the code
/// for tokens, saves the linked account and verifies it against the platform before redirecting back.
/// </summary>
[ApiController]
[Route("api/social/oauth")]
public class SocialOAuthController(AppDbContext db, ProviderRegistry registry, IConfiguration config) : ControllerBase
{
    private sealed record PendingAuth(Guid TenantId, SocialPlatform Platform, string WebBase, string? CodeVerifier, DateTime CreatedAt);

    private static readonly ConcurrentDictionary<string, PendingAuth> Pending = new();

    [Authorize]
    [HttpPost("start")]
    public async Task<IActionResult> Start([FromBody] RequestModels.OAuthStartRequest req)
    {
        if (!Enum.TryParse<SocialPlatform>(req.Platform, true, out var platform) || platform == SocialPlatform.None)
            return BadRequest(ApiResponse<object>.Fail($"Unknown platform '{req.Platform}'."));

        var oauth = registry.GetSocialOAuth(platform);
        if (oauth == null)
            return BadRequest(ApiResponse<object>.Fail("One-click sign-in is not supported for this platform."));
        if (!oauth.IsConfigured)
            return BadRequest(ApiResponse<object>.Fail($"One-click sign-in needs app credentials (Social:{platform}:ClientId / ClientSecret) and a registered redirect URI. Otherwise link the account manually with a token."));

        var webBase = config["Social:WebBaseUrl"]?.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(webBase))
            return BadRequest(ApiResponse<object>.Fail("Social:WebBaseUrl is not configured."));

        var redirectUri = RedirectUriFor(platform);
        if (string.IsNullOrWhiteSpace(redirectUri))
            return BadRequest(ApiResponse<object>.Fail("No OAuth redirect URI configured (Social:OAuth:RedirectUri or Social:{Platform}:RedirectUri)."));

        var tenantId = await this.GetTenantIdAsync(db);
        var state = NewState();
        var (url, verifier) = oauth.BuildAuthorizeUrl(state, redirectUri!);

        Prune();
        Pending[state] = new PendingAuth(tenantId, platform, webBase, verifier, DateTime.UtcNow);

        return Ok(ApiResponse<object>.Ok(new { authorizeUrl = url }));
    }

    [HttpGet("callback")]
    public async Task<IActionResult> Callback(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        [FromQuery] string? error_description,
        CancellationToken ct)
    {
        var fallback = (config["Social:WebBaseUrl"] ?? "http://localhost:5260").TrimEnd('/') + "/social";

        if (string.IsNullOrWhiteSpace(state) || !Pending.TryRemove(state, out var pending))
            return Redirect($"{fallback}?oauth=error:{Uri.EscapeDataString("The sign-in attempt is invalid or expired. Please try again.")}");

        if (!string.IsNullOrWhiteSpace(error))
        {
            var detail = string.IsNullOrWhiteSpace(error_description) ? error : $"{error}: {error_description}";
            return Redirect($"{pending.WebBase}/social?oauth={pending.Platform}:error:{Uri.EscapeDataString(detail)}");
        }

        if (string.IsNullOrWhiteSpace(code))
            return Redirect($"{pending.WebBase}/social?oauth={pending.Platform}:error:{Uri.EscapeDataString("No authorization code was returned by the platform.")}");

        try
        {
            var oauth = registry.GetSocialOAuth(pending.Platform);
            var redirectUri = RedirectUriFor(pending.Platform)!;
            var token = await oauth!.ExchangeAsync(code, redirectUri, pending.CodeVerifier, ct);

            var existing = await db.SocialAccounts.FirstOrDefaultAsync(a => a.TenantId == pending.TenantId && a.Platform == pending.Platform, ct);
            var account = existing ?? new SocialAccount
            {
                Id = Guid.NewGuid(),
                TenantId = pending.TenantId,
                Platform = pending.Platform,
                AccountName = pending.Platform.ToString(),
                AccountHandle = "connected",
                IsDefaultForPlatform = false,
                Status = SocialAccountStatus.Active
            };

            account.AccessToken = token.AccessToken;
            account.RefreshToken = token.RefreshToken;
            account.TokenSecret = token.TokenSecret;
            account.TokenExpiry = token.ExpiresInSeconds is > 0 ? DateTime.UtcNow.AddSeconds(token.ExpiresInSeconds.Value) : null;
            db.SocialAccounts.Update(account);

            var provider = registry.GetSocialProvider(pending.Platform);
            if (provider != null)
            {
                try
                {
                    await provider.ValidateAccountAsync(account, ct);
                    account.Status = SocialAccountStatus.Active;
                    account.LastVerifiedAt = DateTime.UtcNow;
                }
                catch (Exception ex)
                {
                    account.Status = SocialAccountStatus.Error;
                    await db.SaveChangesAsync(ct);
                    return Redirect($"{pending.WebBase}/social?oauth={pending.Platform}:error:{Uri.EscapeDataString($"Signed in but the account could not be used for posting: {ex.Message}")}");
                }
            }

            await db.SaveChangesAsync(ct);
            return Redirect($"{pending.WebBase}/social?oauth={pending.Platform}:ok");
        }
        catch (Exception ex)
        {
            return Redirect($"{pending.WebBase}/social?oauth={pending.Platform}:error:{Uri.EscapeDataString(ex.Message)}");
        }
    }

    private string? RedirectUriFor(SocialPlatform platform)
        => config[$"Social:{platform}:RedirectUri"] ?? config["Social:OAuth:RedirectUri"];

    private static string NewState()
    {
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(16);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static void Prune()
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-10);
        foreach (var kv in Pending)
            if (kv.Value.CreatedAt < cutoff && Pending.TryRemove(kv.Key, out _)) { }
    }
}