using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using TicketFlow.Domain.Events;
using TicketFlow.Domain.Orders;
using TicketFlow.Domain.Tickets;
using TicketFlow.Domain.Users;

namespace TicketFlow.Application.Abstractions;

/// <summary>
/// Unit of work exposed to use cases. EF Core's DbContext already implements the
/// repository + unit-of-work patterns, so we do not wrap it in another generic repository.
/// </summary>
public interface IAppDbContext
{
    DbSet<User> Users { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<Event> Events { get; }
    DbSet<TicketTier> TicketTiers { get; }
    DbSet<Order> Orders { get; }
    DbSet<Ticket> Tickets { get; }

    DatabaseFacade Database { get; }
    ChangeTracker ChangeTracker { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
