using AIPhilosophy.Core.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AIPhilosophy.Infrastructure.Data;

public class AppDbContext : IdentityDbContext<ApplicationUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Script> Scripts => Set<Script>();
    public DbSet<SocialAccount> SocialAccounts => Set<SocialAccount>();
    public DbSet<VideoJob> VideoJobs => Set<VideoJob>();
    public DbSet<Post> Posts => Set<Post>();
    public DbSet<ScheduleRule> ScheduleRules => Set<ScheduleRule>();
    public DbSet<SystemLog> SystemLogs => Set<SystemLog>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        var utc = new ValueConverter<DateTime, DateTime>(
            v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        var utcNullable = new ValueConverter<DateTime?, DateTime?>(
            v => v.HasValue ? (v.Value.Kind == DateTimeKind.Utc ? v.Value : v.Value.ToUniversalTime()) : v,
            v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);

        foreach (var entity in builder.Model.GetEntityTypes())
        {
            foreach (var prop in entity.GetProperties().Where(p => p.ClrType == typeof(DateTime) || p.ClrType == typeof(DateTime?)))
            {
                prop.SetValueConverter(prop.ClrType == typeof(DateTime) ? utc : utcNullable);
            }
        }

        builder.Entity<ApplicationUser>()
            .HasOne(u => u.Tenant)
            .WithMany(t => t.Users)
            .HasForeignKey(u => u.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Tenant>(t =>
        {
            t.HasIndex(x => x.Slug).IsUnique();
            t.Property(x => x.Tier).HasConversion<string>();
        });

        builder.Entity<Script>(s =>
        {
            s.HasIndex(x => new { x.TenantId, x.DayNumber }).IsUnique();
            s.Property(x => x.Status).HasConversion<string>();
            s.HasOne(x => x.Tenant).WithMany(x => x.Scripts).HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<SocialAccount>(a =>
        {
            a.Property(x => x.Platform).HasConversion<string>();
            a.Property(x => x.Status).HasConversion<string>();
            a.HasIndex(x => new { x.TenantId, x.Platform, x.AccessToken }).IsUnique(false);
            a.HasOne(x => x.Tenant).WithMany(x => x.SocialAccounts).HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<VideoJob>(v =>
        {
            v.Property(x => x.Provider).HasConversion<string>();
            v.Property(x => x.Status).HasConversion<string>();
            v.HasIndex(x => new { x.TenantId, x.CreatedAt });
            v.HasOne(x => x.Tenant).WithMany(x => x.VideoJobs).HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.NoAction);
            v.HasOne(x => x.Script).WithMany(x => x.VideoJobs).HasForeignKey(x => x.ScriptId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<Post>(p =>
        {
            p.Property(x => x.Platform).HasConversion<string>();
            p.Property(x => x.Status).HasConversion<string>();
            p.HasIndex(x => new { x.TenantId, x.Status, x.ScheduledAt });
            p.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.NoAction);
            p.HasOne(x => x.SocialAccount).WithMany(x => x.Posts).HasForeignKey(x => x.SocialAccountId).OnDelete(DeleteBehavior.SetNull);
            p.HasOne(x => x.VideoJob).WithMany(x => x.Posts).HasForeignKey(x => x.VideoJobId).OnDelete(DeleteBehavior.SetNull);
            p.HasOne(x => x.Script).WithMany(x => x.Posts).HasForeignKey(x => x.ScriptId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<ScheduleRule>(r =>
        {
            r.Property(x => x.IsEnabled).HasDefaultValue(true);
            r.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
        });

        builder.Entity<SystemLog>()
            .HasIndex(x => new { x.CreatedAt });
    }
}