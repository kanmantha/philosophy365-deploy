using AIPhilosophy.Core.Domain;
using AIPhilosophy.Core.Interfaces;
using AIPhilosophy.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIPhilosophy.Infrastructure.Services;

public class TenantService : ITenantService
{
    private readonly AppDbContext _db;
    private readonly ILogger<TenantService> _logger;

    public TenantService(AppDbContext db, ILogger<TenantService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<Tenant> GetTenantAsync(Guid tenantId, CancellationToken ct = default)
    {
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        return tenant ?? throw new InvalidOperationException($"Tenant {tenantId} not found.");
    }

    public Task<bool> CanCreateVideoAsync(Guid tenantId, CancellationToken ct = default)
        => CheckLimit(tenantId, t => t.BypassUsageLimits || t.VideosUsedThisMonth < t.TotalVideoLimit, ct);

    public Task<bool> CanPostAsync(Guid tenantId, CancellationToken ct = default)
        => CheckLimit(tenantId, _ => true, ct);

    private async Task<bool> CheckLimit(Guid tenantId, Func<Tenant, bool> predicate, CancellationToken ct)
    {
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        return tenant != null && tenant.IsActive && predicate(tenant);
    }

    public async Task RecordVideoUsageAsync(Guid tenantId, CancellationToken ct = default)
    {
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant == null) return;

        if (tenant.TierRenewalDate <= DateTime.UtcNow)
        {
            tenant.VideosUsedThisMonth = 0;
            tenant.TierRenewalDate = DateTime.UtcNow.AddMonths(1);
        }
        tenant.VideosUsedThisMonth++;
        await _db.SaveChangesAsync(ct);
    }

    public async Task ResetMonthlyUsageAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var tenants = await _db.Tenants
            .Where(t => t.TierRenewalDate <= now)
            .ToListAsync(ct);

        foreach (var t in tenants)
        {
            t.VideosUsedThisMonth = 0;
            t.TierRenewalDate = now.AddMonths(1);
        }

        if (tenants.Count > 0)
        {
            _logger.LogInformation("Reset monthly usage for {Count} tenants", tenants.Count);
            await _db.SaveChangesAsync(ct);
        }
    }
}