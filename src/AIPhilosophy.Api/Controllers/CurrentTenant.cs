using System.Security.Claims;
using AIPhilosophy.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AIPhilosophy.Api.Controllers;

public static class CurrentTenant
{
    public static async Task<Guid> GetTenantIdAsync(this ControllerBase controller, AppDbContext db)
    {
        var userId = controller.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Guid.Empty;

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        return user?.TenantId ?? Guid.Empty;
    }
}