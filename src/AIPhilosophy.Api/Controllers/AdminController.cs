using System.Security.Claims;
using AIPhilosophy.Api.Models;
using AIPhilosophy.Core.Domain;
using AIPhilosophy.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AIPhilosophy.Api.Controllers;

[Authorize(Roles = "Admin")]
[ApiController]
[Route("api/admin")]
public class AdminController(
    AppDbContext db,
    UserManager<ApplicationUser> userManager,
    IConfiguration config) : ControllerBase
{
    [HttpGet("stats")]
    public async Task<IActionResult> Stats()
    {
        var now = DateTime.UtcNow;
        var stats = new
        {
            tenants = await db.Tenants.CountAsync(),
            scripts = await db.Scripts.CountAsync(),
            videos = await db.VideoJobs.CountAsync(),
            videosReady = await db.VideoJobs.CountAsync(v => v.Status == VideoStatus.Ready),
            posts = await db.Posts.CountAsync(),
            postsPosted = await db.Posts.CountAsync(p => p.Status == PostStatus.Posted),
            postsFailed = await db.Posts.CountAsync(p => p.Status == PostStatus.Failed),
            schedules = await db.ScheduleRules.CountAsync(r => r.IsEnabled)
        };
        return Ok(ApiResponse<object>.Ok(stats));
    }

    [HttpPost("reset-free-usage")]
    public async Task<IActionResult> ResetFreeUsage()
    {
        // Everyone stays free forever: reset all tenants' monthly usage counters.
        var tenants = await db.Tenants.ToListAsync();
        foreach (var t in tenants)
        {
            t.VideosUsedThisMonth = 0;
            t.TierRenewalDate = DateTime.UtcNow.AddMonths(1);
        }
        await db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(new { reset = tenants.Count }));
    }

[HttpGet("system-logs")]
    public async Task<IActionResult> Logs([FromQuery] int take = 50)
    {
        var logs = await db.SystemLogs
            .OrderByDescending(l => l.CreatedAt)
            .Take(Math.Clamp(take, 1, 500))
            .Select(l => new { l.Id, l.Level, l.Source, l.Message, l.Details, l.CreatedAt })
            .ToListAsync();
        return Ok(ApiResponse<object>.Ok(logs));
    }

    [HttpGet("users")]
    public async Task<IActionResult> Users()
    {
        var users = await userManager.Users
            .OrderByDescending(u => u.CreatedAt)
            .Select(u => new
            {
                u.Id,
                u.Email,
                u.DisplayName,
                u.TenantId,
                u.CreatedAt,
                tenant = db.Tenants.Where(t => t.Id == u.TenantId)
                    .Select(t => new { t.Name, t.BypassUsageLimits, t.TotalVideoLimit, t.VideosUsedThisMonth, t.IsActive })
                    .FirstOrDefault()
            })
            .ToListAsync();

        var items = new List<object>();
        foreach (var u in users)
        {
            var appUser = await userManager.FindByIdAsync(u.Id);
            var roles = appUser != null ? await userManager.GetRolesAsync(appUser) : new List<string>();
            var locked = appUser != null && await userManager.IsLockedOutAsync(appUser);

            items.Add(new
            {
                u.Id,
                u.Email,
                u.DisplayName,
                u.TenantId,
                u.CreatedAt,
                roles,
                isLocked = locked,
                tenantName = (string?)u.tenant?.Name,
                bypassUsageLimits = u.tenant?.BypassUsageLimits ?? false,
                videoLimit = u.tenant?.TotalVideoLimit ?? 0,
                videosUsed = u.tenant?.VideosUsedThisMonth ?? 0,
                tenantActive = u.tenant?.IsActive ?? false
            });
        }

        return Ok(ApiResponse<object>.Ok(items));
    }

    [HttpPost("users")]
    public async Task<IActionResult> CreateUser([FromBody] RequestModels.CreateUserRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
            return BadRequest(ApiResponse<object>.Fail("Email and password are required."));
        if (req.Password.Length < 8)
            return BadRequest(ApiResponse<object>.Fail("Password must be at least 8 characters."));

        var existing = await userManager.FindByEmailAsync(req.Email.Trim());
        if (existing != null)
            return Conflict(ApiResponse<object>.Fail("An account with this email already exists."));

        var role = string.IsNullOrWhiteSpace(req.Role) || !"Admin".Equals(req.Role.Trim(), StringComparison.OrdinalIgnoreCase)
            ? "User"
            : "Admin";

        var tenant = new Tenant
        {
            Name = string.IsNullOrWhiteSpace(req.TenantName) ? $"Workspace - {req.Email.Trim()}" : req.TenantName.Trim(),
            Slug = $"ws-{Guid.NewGuid():N}"[..12],
            Tier = SubscriptionTier.Free,
            TotalVideoLimit = 3,
            DailyPostLimit = 5
        };
        db.Tenants.Add(tenant);

        var user = new ApplicationUser
        {
            UserName = req.Email.Trim().Split('@')[0] + Guid.NewGuid().ToString("N")[..4],
            Email = req.Email.Trim(),
            DisplayName = string.IsNullOrWhiteSpace(req.DisplayName) ? req.Email.Trim().Split('@')[0] : req.DisplayName.Trim(),
            TenantId = tenant.Id,
            EmailConfirmed = true
        };

        var result = await userManager.CreateAsync(user, req.Password);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<object>.Fail(string.Join("; ", result.Errors.Select(e => e.Description))));

        await userManager.AddToRoleAsync(user, role);
        return Ok(ApiResponse<object>.Ok(new { userId = user.Id, tenantId = tenant.Id, role }, $"User {user.Email} created with role {role}."));
    }

    [HttpPut("users/{id:guid}")]
    public async Task<IActionResult> UpdateUser(Guid id, [FromBody] RequestModels.UpdateUserRequest req)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user == null) return NotFound(ApiResponse<object>.Fail("User not found."));

        if (!string.IsNullOrWhiteSpace(req.DisplayName))
            user.DisplayName = req.DisplayName.Trim();

        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<object>.Fail(string.Join("; ", result.Errors.Select(e => e.Description))));

        return Ok(ApiResponse<object>.Ok(new { id, user.DisplayName }, "User updated."));
    }

    [HttpDelete("users/{id:guid}")]
    public async Task<IActionResult> DeleteUser(Guid id)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user == null) return NotFound(ApiResponse<object>.Fail("User not found."));

        if (IsProtectsSeedAdmin(user))
            return BadRequest(ApiResponse<object>.Fail("The system administrator cannot be deleted."));

        var callerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (callerId == user.Id.ToString())
            return BadRequest(ApiResponse<object>.Fail("You cannot delete your own account here."));

        var result = await userManager.DeleteAsync(user);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<object>.Fail(string.Join("; ", result.Errors.Select(e => e.Description))));

        return Ok(ApiResponse<object>.Ok(new { id }, $"User {user.Email} deleted."));
    }

    [HttpPost("users/{id:guid}/role")]
    public async Task<IActionResult> SetRole(Guid id, [FromBody] RequestModels.UserRoleRequest req)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user == null) return NotFound(ApiResponse<object>.Fail("User not found."));

        var role = req.Role?.Trim();
        if (role != "Admin" && role != "User")
            return BadRequest(ApiResponse<object>.Fail("Role must be 'Admin' or 'User'."));

        if (IsProtectsSeedAdmin(user))
        {
            if (role != "Admin")
                return BadRequest(ApiResponse<object>.Fail("The system administrator cannot be demoted."));
            return Ok(ApiResponse<object>.Ok(new { id, roles = new[] { "Admin" } }));
        }

        var current = await userManager.GetRolesAsync(user);
        if (current.Contains(role, StringComparer.OrdinalIgnoreCase))
            return Ok(ApiResponse<object>.Ok(new { id, roles = current }));

        await userManager.RemoveFromRolesAsync(user, current);
        await userManager.AddToRoleAsync(user, role);
        return Ok(ApiResponse<object>.Ok(new { id, roles = await userManager.GetRolesAsync(user) }));
    }

    [HttpPost("users/{id:guid}/enable")]
    public async Task<IActionResult> EnableUser(Guid id)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user == null) return NotFound(ApiResponse<object>.Fail("User not found."));

        await userManager.SetLockoutEndDateAsync(user, null);
        return Ok(ApiResponse<object>.Ok(new { id, enabled = true }));
    }

    [HttpPost("users/{id:guid}/disable")]
    public async Task<IActionResult> DisableUser(Guid id)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user == null) return NotFound(ApiResponse<object>.Fail("User not found."));

        if (IsProtectsSeedAdmin(user))
            return BadRequest(ApiResponse<object>.Fail("The system administrator cannot be disabled."));

        await userManager.SetLockoutEnabledAsync(user, true);
        await userManager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddYears(100));
        return Ok(ApiResponse<object>.Ok(new { id, enabled = false }));
    }

    [HttpPost("users/{id:guid}/reset-password")]
    public async Task<IActionResult> ResetPassword(Guid id, [FromBody] RequestModels.ResetPasswordRequest req)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user == null) return NotFound(ApiResponse<object>.Fail("User not found."));

        if (string.IsNullOrWhiteSpace(req.NewPassword) || req.NewPassword.Length < 8)
            return BadRequest(ApiResponse<object>.Fail("Password must be at least 8 characters."));

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var result = await userManager.ResetPasswordAsync(user, token, req.NewPassword);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<object>.Fail(string.Join("; ", result.Errors.Select(e => e.Description))));

        await userManager.SetLockoutEndDateAsync(user, null);
        return Ok(ApiResponse<object>.Ok(new { id, reset = true }, "Password updated."));
    }

    private bool IsProtectsSeedAdmin(ApplicationUser user)
    {
        var seedEmail = config["Seed:AdminEmail"] ?? "admin@philosophy365.app";
        return string.Equals(user.Email, seedEmail, StringComparison.OrdinalIgnoreCase);
    }
}
