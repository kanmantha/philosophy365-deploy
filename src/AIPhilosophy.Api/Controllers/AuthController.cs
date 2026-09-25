using System.Security.Claims;
using AIPhilosophy.Api.Models;
using AIPhilosophy.Core.Domain;
using AIPhilosophy.Core.Interfaces;
using AIPhilosophy.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AIPhilosophy.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController(
    UserManager<ApplicationUser> userManager,
    ITokenService tokenService,
    AppDbContext db) : ControllerBase
{
    [HttpPost("register")]
    [ProducesResponseType(typeof(ApiResponse<object>), 200)]
    public async Task<IActionResult> Register([FromBody] RequestModels.RegisterRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
            return BadRequest(ApiResponse<object>.Fail("Email and password are required."));

        var existing = await userManager.FindByEmailAsync(req.Email);
        if (existing != null)
            return Conflict(ApiResponse<object>.Fail("An account with this email already exists."));

        var tenant = new Tenant
        {
            Name = $"Workspace - {req.Email}",
            Slug = $"ws-{Guid.NewGuid():N}"[..12],
            Tier = SubscriptionTier.Free,
            TotalVideoLimit = 3,
            DailyPostLimit = 5
        };
        db.Tenants.Add(tenant);

        var user = new ApplicationUser
        {
            UserName = req.Email.Split('@')[0] + Guid.NewGuid().ToString("N")[..4],
            Email = req.Email,
            DisplayName = string.IsNullOrWhiteSpace(req.DisplayName) ? req.Email.Split('@')[0] : req.DisplayName,
            TenantId = tenant.Id,
            EmailConfirmed = true
        };

        var result = await userManager.CreateAsync(user, req.Password);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<object>.Fail(string.Join("; ", result.Errors.Select(e => e.Description))));

        await userManager.AddToRoleAsync(user, "User");
        return Ok(ApiResponse<object>.Ok(new { userId = user.Id, tenantId = tenant.Id }, "Free account created. 3 free AI videos included."));
    }

    [HttpPost("login")]
    [ProducesResponseType(typeof(ApiResponse<object>), 200)]
    public async Task<IActionResult> Login([FromBody] RequestModels.LoginRequest req)
    {
        var user = await userManager.FindByEmailAsync(req.Email);
        if (user == null || !await userManager.CheckPasswordAsync(user, req.Password))
            return Unauthorized(ApiResponse<object>.Fail("Invalid email or password."));

        if (await userManager.IsLockedOutAsync(user))
            return Unauthorized(ApiResponse<object>.Fail("This account is disabled. Contact a system administrator."));

        var roles = await userManager.GetRolesAsync(user);
        var token = tokenService.CreateToken(user, roles);
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == user.TenantId);

        return Ok(ApiResponse<object>.Ok(new
        {
            token,
            user.DisplayName,
            user.Email,
            tenantId = user.TenantId,
            tier = tenant?.Tier.ToString(),
            videosRemaining = Math.Max(0, (tenant?.TotalVideoLimit ?? 0) - (tenant?.VideosUsedThisMonth ?? 0))
        }));
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = await userManager.FindByIdAsync(userId ?? string.Empty);
        if (user == null) return Unauthorized(ApiResponse<object>.Fail("Not logged in."));

        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == user.TenantId);
        var roles = await userManager.GetRolesAsync(user);

        return Ok(ApiResponse<object>.Ok(new
        {
            user.DisplayName,
            user.Email,
            roles,
            tenantId = user.TenantId,
            tier = tenant?.Tier.ToString(),
            videosRemaining = Math.Max(0, (tenant?.TotalVideoLimit ?? 0) - (tenant?.VideosUsedThisMonth ?? 0))
        }));
    }
}