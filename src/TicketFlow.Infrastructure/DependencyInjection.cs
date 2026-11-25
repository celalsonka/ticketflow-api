using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using TicketFlow.Application.Abstractions;
using TicketFlow.Application.Common;
using TicketFlow.Infrastructure.BackgroundJobs;
using TicketFlow.Infrastructure.Payments;
using TicketFlow.Infrastructure.Persistence;
using TicketFlow.Infrastructure.Security;

namespace TicketFlow.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);

        services.AddOptions<TicketingOptions>()
            .Bind(configuration.GetSection(TicketingOptions.SectionName))
            .Validate(o => o.ReservationHoldMinutes > 0 && o.MaxTicketsPerOrder > 0 && o.ExpirationSweepSeconds > 0,
                "Ticketing settings must be positive.")
            .ValidateOnStart();

        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(o => o.Secret.Length >= 32, "Jwt:Secret must be at least 32 characters long.")
            .Validate(o => o.AccessTokenMinutes > 0 && o.RefreshTokenDays > 0, "Jwt lifetimes must be positive.")
            .ValidateOnStart();

        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName))
            .ValidateOnStart();

        // Resolved lazily so configuration overrides (env vars, tests) are always honoured.
        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            var database = sp.GetRequiredService<IOptions<DatabaseOptions>>().Value;
            var config = sp.GetRequiredService<IConfiguration>();

            if (database.Provider == DatabaseProvider.Postgres)
            {
                options.UseNpgsql(
                    config.GetConnectionString("Postgres")
                    ?? throw new InvalidOperationException("ConnectionStrings:Postgres is not configured."));
            }
            else
            {
                options.UseSqlite(config.GetConnectionString("Sqlite") ?? "Data Source=ticketflow.db");
            }
        });
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddSingleton<IPasswordHasher, IdentityPasswordHasher>();
        services.AddSingleton<IPaymentGateway, FakePaymentGateway>();

        services.AddHostedService<OrderExpirationWorker>();

        return services;
    }
}
