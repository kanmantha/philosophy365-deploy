using AIPhilosophy.Core.Domain;
using AIPhilosophy.Infrastructure.Data;
using AIPhilosophy.Infrastructure.Seed;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AIPhilosophy.Infrastructure.Services;

public class DataSeeder(
    AppDbContext db,
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole> roleManager,
    IConfiguration config,
    ILogger<DataSeeder> logger)
{
    public async Task SeedAsync()
    {
        await EnsureRolesAsync();

        var adminEmail = config["Seed:AdminEmail"] ?? "admin@philosophy365.app";
        var adminPassword = config["Seed:AdminPassword"] ?? "Admin@12345!";

        var tenant = await EnsureTenantAndAdminAsync(adminEmail, adminPassword);
        await SeedPhilosophyContentAsync(tenant.Id);
        await EnsureScheduleRuleAsync(tenant.Id);

        logger.LogInformation("Seed completed for tenant {TenantId}", tenant.Id);
    }

    private async Task EnsureRolesAsync()
    {
        foreach (var role in new[] { "Admin", "User" })
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        }
    }

    private async Task<Tenant> EnsureTenantAndAdminAsync(string email, string password)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Slug == "philosophy365");
        if (tenant == null)
        {
            tenant = new Tenant
            {
                Name = "365 Days of Philosophy",
                Slug = "philosophy365",
                Description = "Daily AI-generated philosophy videos - 100% free forever.",
                Tier = SubscriptionTier.Free,
                BypassUsageLimits = true,
                DailyVideoLimit = 3,
                DailyPostLimit = 10,
                TotalVideoLimit = 365
            };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();
            logger.LogInformation("Created default tenant {Slug}", tenant.Slug);
        }
        else if (!tenant.BypassUsageLimits)
        {
            tenant.BypassUsageLimits = true;
            await db.SaveChangesAsync();
            logger.LogInformation("Enabled unlimited usage bypass for default tenant {Slug}", tenant.Slug);
        }

        var admin = await userManager.FindByEmailAsync(email);
        if (admin == null)
        {
            admin = new ApplicationUser
            {
                UserName = email.Split('@')[0],
                Email = email,
                DisplayName = "Content Owner",
                TenantId = tenant.Id,
                EmailConfirmed = true
            };
            var result = await userManager.CreateAsync(admin, password);
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(admin, "Admin");
                logger.LogInformation("Created admin user {Email}", email);
            }
        }
        else if (admin.TenantId == Guid.Empty)
        {
            admin.TenantId = tenant.Id;
            await db.SaveChangesAsync();
        }

        return tenant;
    }

    private async Task SeedPhilosophyContentAsync(Guid tenantId)
    {
        var existing = await db.Scripts.Where(s => s.TenantId == tenantId).CountAsync();
        if (existing > 0) return;

        foreach (var s in SeedContent.Build365())
        {
            db.Scripts.Add(new Script
            {
                TenantId = tenantId,
                DayNumber = s.Day,
                Title = s.Title,
                Subtitle = s.Subtitle,
                Body = s.Body,
                VideoPrompt = s.VideoPrompt,
                Description = s.Description,
                Hashtags = s.Hashtags,
                Category = s.Category,
                Philosopher = s.Philosopher,
                EstimatedDurationSeconds = 45,
                VoiceStyle = "Warm, Calm, Reflective",
                Status = ScriptStatus.Ready,
                ScheduledPublishDate = DateTime.UtcNow.Date.AddSeconds(s.Day * 86400)
            });
        }

        await db.SaveChangesAsync();
        logger.LogInformation("Seeded 365 philosophy scripts for tenant {TenantId}", tenantId);
    }

    private async Task EnsureScheduleRuleAsync(Guid tenantId)
    {
        if (await db.ScheduleRules.AnyAsync(r => r.TenantId == tenantId)) return;

        db.ScheduleRules.Add(new ScheduleRule
        {
            TenantId = tenantId,
            Name = "Default Daily Pipeline",
            GenerationHourUTC = 6,
            PostHourUTC = 12,
            PostingDaysCsv = "Mon,Tue,Wed,Thu,Fri,Sat,Sun",
            PostingTimesCsv = "09:00",
            TargetPlatformsCsv = "YouTubeShorts,TikTok,InstagramReels",
            TimeZoneId = "UTC"
        });

        await db.SaveChangesAsync();
        logger.LogInformation("Created default schedule rule for tenant {TenantId}", tenantId);
    }
}