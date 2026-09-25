using AIPhilosophy.Api.Models;
using AIPhilosophy.Core.Domain;
using AIPhilosophy.Infrastructure.Data;
using AIPhilosophy.Infrastructure.Providers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace AIPhilosophy.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/social")]
public class SocialController(AppDbContext db, ProviderRegistry registry, IConfiguration config) : ControllerBase
{
    [HttpGet("platforms")]
    public async Task<IActionResult> Platforms()
    {
        var tenantId = await this.GetTenantIdAsync(db);
        var demoMode = string.Equals(config["Social:DemoMode"], "true", StringComparison.OrdinalIgnoreCase);
        var platforms = new List<object>();

        foreach (var p in Enum.GetValues<SocialPlatform>().Where(p => p != SocialPlatform.None))
        {
            var provider = registry.GetSocialProvider(p);
            var hasLinkedToken = await db.SocialAccounts.AnyAsync(a => a.TenantId == tenantId && a.Platform == p && !string.IsNullOrWhiteSpace(a.AccessToken));
            var isReal = provider != null;
            platforms.Add(new
            {
                platform = p.ToString(),
                name = p.ToString(),
                isMock = !isReal,
                isConfigured = provider?.IsConfigured ?? false,
                requiresSetup = isReal && !(provider?.IsConfigured ?? false) && !hasLinkedToken,
                hasLinkedToken,
                simulated = demoMode && (!isReal || !(provider?.IsConfigured ?? false)),
                mode = isReal ? "real" : "demo"
            });
        }

        return Ok(ApiResponse<object>.Ok(platforms));
    }

    [HttpGet("accounts")]
    public async Task<IActionResult> Accounts()
    {
        var tenantId = await this.GetTenantIdAsync(db);
        var accounts = await db.SocialAccounts
            .Where(a => a.TenantId == tenantId)
            .Select(a => new
            {
a.Id,
                platform = a.Platform.ToString(),
                a.AccountName,
                a.AccountHandle,
                a.IsDefaultForPlatform,
                status = a.Status.ToString(),
                hasToken = !string.IsNullOrWhiteSpace(a.AccessToken),
                a.LastVerifiedAt
            })
            .ToListAsync();
        return Ok(ApiResponse<object>.Ok(accounts));
    }

[HttpPost("accounts")]
    public async Task<IActionResult> AddAccount([FromBody] RequestModels.SocialAccountRequest req)
    {
        if (!Enum.TryParse<SocialPlatform>(req.Platform, true, out var platform) || platform == SocialPlatform.None)
            return BadRequest(ApiResponse<object>.Fail($"Unknown platform '{req.Platform}'."));
        if (string.IsNullOrWhiteSpace(req.AccountName))
            return BadRequest(ApiResponse<object>.Fail("Account name is required."));
        if (string.IsNullOrWhiteSpace(req.AccountHandle))
            return BadRequest(ApiResponse<object>.Fail("Handle / channel id is required."));

        var tenantId = await this.GetTenantIdAsync(db);
        var provider = registry.GetSocialProvider(platform);

        var account = new SocialAccount
        {
            TenantId = tenantId,
            Platform = platform,
            AccountName = req.AccountName.Trim(),
            AccountHandle = req.AccountHandle.Trim(),
            AccessToken = req.AccessToken,
            RefreshToken = req.RefreshToken,
            TokenSecret = req.TokenSecret,
            IsDefaultForPlatform = req.IsDefaultForPlatform,
            Status = SocialAccountStatus.Active
        };

        // Prove the credentials are really valid before persisting an "Active" account.
        if (provider != null && !string.IsNullOrWhiteSpace(req.AccessToken))
        {
            try
            {
                await provider.ValidateAccountAsync(account, HttpContext.RequestAborted);
                account.Status = SocialAccountStatus.Active;
                account.LastVerifiedAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                return BadRequest(ApiResponse<object>.Fail($"Credentials could not be verified: {ex.Message}"));
            }
        }

        if (req.IsDefaultForPlatform)
        {
            var existing = await db.SocialAccounts.Where(a => a.TenantId == tenantId && a.Platform == platform && a.IsDefaultForPlatform).ToListAsync();
            foreach (var e in existing) e.IsDefaultForPlatform = false;
        }

        db.SocialAccounts.Add(account);
        await db.SaveChangesAsync();

        var verified = account.LastVerifiedAt is not null;
        return Ok(ApiResponse<object>.Ok(
            new { account.Id, account.Platform, account.AccountHandle, verified },
            verified
                ? "Account linked and credentials verified with the platform."
                : "Account linked but no token was supplied, so it was not verified against the platform."));
    }

    [HttpDelete("accounts/{id:guid}")]
    public async Task<IActionResult> DeleteAccount(Guid id)
    {
        var tenantId = await this.GetTenantIdAsync(db);
        var account = await db.SocialAccounts.FirstOrDefaultAsync(a => a.Id == id && a.TenantId == tenantId);
        if (account == null) return NotFound();

        db.SocialAccounts.Remove(account);
        await db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(true));
    }

    [HttpPost("accounts/{id:guid}/validate")]
    public async Task<IActionResult> Validate(Guid id)
    {
        var tenantId = await this.GetTenantIdAsync(db);
        var account = await db.SocialAccounts.FirstOrDefaultAsync(a => a.Id == id && a.TenantId == tenantId);
        if (account == null) return NotFound();

try
        {
            var provider = registry.GetSocialProvider(account.Platform);
            if (provider == null)
                return BadRequest(ApiResponse<object>.Fail("No provider is registered for this platform."));

            await provider.ValidateAccountAsync(account, HttpContext.RequestAborted);
            account.LastVerifiedAt = DateTime.UtcNow;
            account.Status = SocialAccountStatus.Active;
            await db.SaveChangesAsync();
            return Ok(ApiResponse<object>.Ok(new { account.Id, verified = true }));
        }
        catch (Exception ex)
        {
            account.Status = SocialAccountStatus.Error;
            await db.SaveChangesAsync();
            return BadRequest(ApiResponse<object>.Fail(ex.Message));
        }
    }
}
