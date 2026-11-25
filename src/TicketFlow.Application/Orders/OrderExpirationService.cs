using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TicketFlow.Application.Abstractions;
using TicketFlow.Domain.Orders;

namespace TicketFlow.Application.Orders;

/// <summary>
/// Releases seats held by reservations that were never paid. Invoked periodically by a background
/// worker; safe to run on several API instances at once thanks to optimistic concurrency on orders.
/// </summary>
public sealed class OrderExpirationService(
    IAppDbContext db,
    TimeProvider timeProvider,
    ILogger<OrderExpirationService> logger)
{
    public async Task<int> ExpireOverdueOrdersAsync(int batchSize, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var overdue = await db.Orders.AsNoTracking()
            .Where(o => o.Status == OrderStatus.Pending && o.ExpiresAt <= now)
            .OrderBy(o => o.ExpiresAt)
            .Select(o => o.Id)
            .Take(batchSize)
            .ToListAsync(ct);

        var expired = 0;
        foreach (var orderId in overdue)
        {
            db.ChangeTracker.Clear();
            var order = await db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == orderId, ct);

            // Paid or cancelled in the meantime, or another instance got there first: nothing to release.
            if (order is not null
                && order.Expire(now).IsSuccess
                && await db.SaveClosedOrderAndReleaseSeatsAsync(order, ct))
            {
                expired++;
            }
        }

        if (expired > 0)
        {
            logger.LogInformation("Expired {Count} unpaid orders and released their seats", expired);
        }

        return expired;
    }
}
