using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using TicketFlow.Infrastructure.Persistence;

namespace TicketFlow.IntegrationTests.Infrastructure;

/// <summary>
/// Boots the real API in memory (full middleware pipeline, auth, validation, EF Core) against an
/// isolated database with a controllable clock.
/// <para>
/// By default each fixture gets its own SQLite file. Set <c>TICKETFLOW_TEST_POSTGRES</c> to a PostgreSQL
/// connection string without a database name (e.g. <c>Host=localhost;Username=postgres;Password=postgres</c>)
/// to run the very same suite against PostgreSQL, applying the real EF Core migrations - CI does this.
/// </para>
/// </summary>
public class TicketFlowApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string PostgresVariable = "TICKETFLOW_TEST_POSTGRES";

    private readonly string _databaseName = $"ticketflow_test_{Guid.NewGuid():N}";
    private readonly string? _postgres = Environment.GetEnvironmentVariable(PostgresVariable);

    private string SqlitePath => Path.Combine(Path.GetTempPath(), $"{_databaseName}.db");

    /// <summary>Starts at the real current time so tokens and schedules look realistic; tests may advance it.</summary>
    public FakeTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);

    public bool UsesPostgres => !string.IsNullOrWhiteSpace(_postgres);

    protected virtual void ConfigureSettings(IDictionary<string, string?> settings)
    {
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Database:SeedDemoData"] = "false",
            ["Jwt:Secret"] = "integration-tests-secret-0123456789-abcdefghijklmnop",
            ["Ticketing:EnableExpirationWorker"] = "false",
            ["Ticketing:MaxTicketsPerOrder"] = "10",
            ["RateLimiting:AuthPermitLimit"] = "100000",
            ["RateLimiting:OrdersTokenLimit"] = "100000",
            ["RateLimiting:OrdersTokensPerMinute"] = "100000",
            ["Serilog:MinimumLevel:Default"] = "Warning",
        };

        if (UsesPostgres)
        {
            settings["Database:Provider"] = "Postgres";
            settings["ConnectionStrings:Postgres"] = $"{_postgres!.TrimEnd(';')};Database={_databaseName}";
        }
        else
        {
            settings["Database:Provider"] = "Sqlite";
            settings["ConnectionStrings:Sqlite"] = $"Data Source={SqlitePath}";
        }

        ConfigureSettings(settings);

        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }

    public Task InitializeAsync()
    {
        _ = Server; // boot the host (creates/migrates the database) before the first test runs
        return Task.CompletedTask;
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        if (UsesPostgres)
        {
            try
            {
                await using var scope = Services.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureDeletedAsync();
            }
#pragma warning disable CA1031 // Clean-up of a throw-away test database is best effort.
            catch (Exception)
#pragma warning restore CA1031
            {
            }
        }

        await DisposeAsync();

        if (!UsesPostgres)
        {
            SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                try
                {
                    File.Delete(SqlitePath + suffix);
                }
                catch (IOException)
                {
                    // Best effort clean-up of temp files.
                }
            }
        }
    }
}
