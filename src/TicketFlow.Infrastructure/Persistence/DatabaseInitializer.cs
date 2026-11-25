using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TicketFlow.Application.Abstractions;

namespace TicketFlow.Infrastructure.Persistence;

public enum DatabaseProvider
{
    /// <summary>Zero-setup file database for local development and tests.</summary>
    Sqlite,

    /// <summary>Production database (see docker-compose.yml).</summary>
    Postgres,
}

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public DatabaseProvider Provider { get; set; } = DatabaseProvider.Sqlite;

    /// <summary>Seeds demo users and events on an empty database.</summary>
    public bool SeedDemoData { get; set; }
}

public static class DatabaseInitializer
{
    /// <summary>
    /// PostgreSQL is upgraded with EF Core migrations; SQLite (dev/test) is created from the model.
    /// </summary>
    public static async Task InitializeDatabaseAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var db = provider.GetRequiredService<AppDbContext>();
        var options = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatabaseInitializer));

        if (options.Provider == DatabaseProvider.Postgres)
        {
            logger.LogInformation("Applying PostgreSQL migrations");
            await db.Database.MigrateAsync(ct);
        }
        else
        {
            await db.Database.EnsureCreatedAsync(ct);
            // WAL lets readers proceed while a writer holds the lock - much better under concurrent requests.
            await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", ct);
        }

        if (options.SeedDemoData)
        {
            await DemoDataSeeder.SeedAsync(
                db,
                provider.GetRequiredService<IPasswordHasher>(),
                provider.GetRequiredService<TimeProvider>(),
                logger,
                ct);
        }
    }
}
