using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TicketFlow.Infrastructure.Persistence;

/// <summary>
/// Used only by `dotnet ef migrations ...`. Migrations target PostgreSQL (the production database).
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=ticketflow;Username=ticketflow;Password=ticketflow")
            .Options;

        return new AppDbContext(options);
    }
}
