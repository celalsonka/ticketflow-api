using Microsoft.EntityFrameworkCore;
using TicketFlow.Application.Abstractions;
using TicketFlow.Domain.Events;
using TicketFlow.Domain.Orders;

namespace TicketFlow.Application.Orders;

/// <summary>Atomic seat-inventory operations shared by the order use cases.</summary>
internal static class InventoryExtensions
{
    /// <summary>
    /// Takes <paramref name="quantity"/> seats from a tier with ONE conditional statement:
    /// <c>UPDATE TicketTiers SET Sold = Sold + @q WHERE Id = @id AND Sold + @q &lt;= Capacity AND event is published</c>.
    /// The database checks and writes atomically (row lock), so two buyers can never both get the last seat,
    /// without SERIALIZABLE isolation or application-level locks. Returns false when not enough seats are left.
    /// </summary>
    public static async Task<bool> TryReserveSeatsAsync(this IAppDbContext db, Guid tierId, int quantity, CancellationToken ct)
    {
        var updated = await db.TicketTiers
            .Where(t => t.Id == tierId
                && t.Sold + quantity <= t.Capacity
                && db.Events.Any(e => e.Id == t.EventId && e.Status == EventStatus.Published))
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.Sold, t => t.Sold + quantity), ct);

        return updated == 1;
    }

    /// <summary>Returns the seats held by an order to the pool.</summary>
    public static async Task ReleaseSeatsAsync(this IAppDbContext db, Order order, CancellationToken ct)
    {
        foreach (var item in order.Items)
        {
            var tierId = item.TicketTierId;
            var quantity = item.Quantity;
            await db.TicketTiers
                .Where(t => t.Id == tierId)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.Sold, t => t.Sold - quantity), ct);
        }
    }

    /// <summary>
    /// Persists a pending order's transition to Cancelled/Expired and releases its seats in one transaction.
    /// The order is saved first: its concurrency token guarantees the seats are released at most once,
    /// even if the sweeper, the customer and a payment race each other.
    /// </summary>
    public static async Task<bool> SaveClosedOrderAndReleaseSeatsAsync(this IAppDbContext db, Order order, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(ct);
            return false;
        }

        await db.ReleaseSeatsAsync(order, ct);
        await transaction.CommitAsync(ct);
        return true;
    }
}
