using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AIPhilosophy.Infrastructure.Data;

public static class DbRegistration
{
    public static bool UsesPostgres(this IConfiguration config)
        => (config["Database:Provider"] ?? "SqlServer").Equals("Postgres", StringComparison.OrdinalIgnoreCase);

    public static void AddAppDbContext(this IServiceCollection services, IConfiguration config)
    {
        var connString = config.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

        services.AddDbContext<AppDbContext>(o =>
        {
            if (config.UsesPostgres())
                o.UseNpgsql(connString, s => s.EnableRetryOnFailure(3));
            else
                o.UseSqlServer(connString, s => s.EnableRetryOnFailure(3));
        });
    }

    public static async Task EnsureSchemaAsync(this AppDbContext db, bool useMigrations, ILogger logger, CancellationToken ct = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (useMigrations)
                    await db.Database.MigrateAsync(ct);
                else
                    await db.Database.EnsureCreatedAsync(ct);
                return;
            }
            catch (Exception ex)
            {
                if (attempt >= 5) throw;
                logger.LogWarning(ex, "Schema initialization attempt {Attempt} failed; retrying in 3s", attempt);
                await Task.Delay(TimeSpan.FromSeconds(3), ct);
            }
        }
    }
}