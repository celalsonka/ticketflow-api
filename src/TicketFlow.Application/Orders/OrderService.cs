using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TicketFlow.Application.Abstractions;
using TicketFlow.Application.Common;
using TicketFlow.Domain.Common;
using TicketFlow.Domain.Events;
using TicketFlow.Domain.Orders;

namespace TicketFlow.Application.Orders;

public sealed class OrderService(
    IAppDbContext db,
    ICurrentUser currentUser,
    IPaymentGateway paymentGateway,
    TimeProvider timeProvider,
    IOptions<TicketingOptions> options,
    ILogger<OrderService> logger)
{
    /// <summary>
    /// Reserves seats and creates a pending order that must be paid within the reservation window.
    /// Overselling is prevented by <see cref="InventoryExtensions.TryReserveSeatsAsync"/>; retries are made
    /// safe by the optional Idempotency-Key (the original order is returned instead of a duplicate).
    /// </summary>
    public async Task<Result<OrderPlacement>> PlaceAsync(CreateOrderRequest request, string? idempotencyKey, CancellationToken ct)
    {
        var userId = currentUser.UserId;
        var settings = options.Value;
        var now = timeProvider.GetUtcNow();

        if (idempotencyKey is not null && await FindByIdempotencyKeyAsync(userId, idempotencyKey, ct) is { } previous)
        {
            return new OrderPlacement(previous, Replayed: true);
        }

        var lines = request.Items
            .GroupBy(i => i.TicketTierId)
            .Select(g => (TierId: g.Key, Quantity: g.Sum(i => i.Quantity)))
            .ToList();

        if (lines.Sum(l => l.Quantity) > settings.MaxTicketsPerOrder)
        {
            return OrderErrors.TooManyTickets(settings.MaxTicketsPerOrder);
        }

        var @event = await db.Events.AsNoTracking()
            .Include(e => e.TicketTiers)
            .FirstOrDefaultAsync(e => e.Id == request.EventId, ct);

        if (@event is null)
        {
            return EventErrors.NotFound;
        }

        if (!@event.IsOnSale(now))
        {
            return EventErrors.NotOnSale;
        }

        var tiers = @event.TicketTiers.ToDictionary(t => t.Id);
        var orderLines = new List<(TicketTier Tier, int Quantity)>(lines.Count);
        foreach (var (tierId, quantity) in lines)
        {
            if (!tiers.TryGetValue(tierId, out var tier))
            {
                return OrderErrors.TierNotInEvent;
            }

            orderLines.Add((tier, quantity));
        }

        var created = Order.Create(userId, @event.Id, orderLines, settings.Currency, now,
            TimeSpan.FromMinutes(settings.ReservationHoldMinutes), idempotencyKey);
        if (created.IsFailure)
        {
            return created.Error;
        }

        var order = created.Value;

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        foreach (var (tier, quantity) in orderLines)
        {
            if (!await db.TryReserveSeatsAsync(tier.Id, quantity, ct))
            {
                await transaction.RollbackAsync(ct);
                return OrderErrors.SoldOut(tier.Name);
            }
        }

        db.Orders.Add(order);
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException) when (idempotencyKey is not null)
        {
            // A concurrent request with the same Idempotency-Key won the race (unique index).
            // Roll back our seat reservation and hand back the winner's order.
            await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            if (await FindByIdempotencyKeyAsync(userId, idempotencyKey, ct) is { } winner)
            {
                return new OrderPlacement(winner, Replayed: true);
            }

            throw;
        }

        logger.LogInformation(
            "Order {OrderId} placed for event {EventId}: {Quantity} tickets, {Amount} {Currency}",
            order.Id, @event.Id, lines.Sum(l => l.Quantity), order.TotalAmount, order.Currency);

        return new OrderPlacement(OrderDto.From(order, @event.Title), Replayed: false);
    }

    public async Task<Result<OrderDto>> PayAsync(Guid orderId, PayOrderRequest request, CancellationToken ct)
    {
        var order = await LoadOwnedOrderAsync(orderId, tracking: true, ct);
        if (order is null)
        {
            return OrderErrors.NotFound;
        }

        var now = timeProvider.GetUtcNow();
        if (order.IsExpired(now))
        {
            // The sweeper has not processed it yet; do it now so the seats go back on sale immediately.
            if (order.Expire(now).IsSuccess)
            {
                await db.SaveClosedOrderAndReleaseSeatsAsync(order, ct);
            }

            return OrderErrors.ReservationExpired;
        }

        if (order.Status != OrderStatus.Pending)
        {
            return OrderErrors.NotPending(order.Status);
        }

        var payment = await paymentGateway.ChargeAsync(
            new PaymentRequest(order.Id, order.TotalAmount, order.Currency, request.CardToken), ct);

        if (!payment.Succeeded || payment.Reference is null)
        {
            logger.LogInformation("Payment for order {OrderId} failed: {Reason}", order.Id, payment.FailureReason);
            return OrderErrors.PaymentFailed(payment.FailureReason ?? "unknown error");
        }

        var paid = order.MarkPaid(payment.Reference, now);
        if (paid.IsFailure)
        {
            await paymentGateway.RefundAsync(payment.Reference, order.TotalAmount, order.Currency, ct);
            return paid.Error;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The order changed under us (expired by the sweeper, cancelled, or paid by a parallel request).
            await paymentGateway.RefundAsync(payment.Reference, order.TotalAmount, order.Currency, ct);
            return OrderErrors.ConcurrentModification;
        }

        logger.LogInformation("Order {OrderId} paid; {TicketCount} tickets issued", order.Id, order.Tickets.Count);
        return OrderDto.From(order, order.Event.Title);
    }

    public async Task<Result<OrderDto>> CancelAsync(Guid orderId, CancellationToken ct)
    {
        var order = await LoadOwnedOrderAsync(orderId, tracking: true, ct);
        if (order is null)
        {
            return OrderErrors.NotFound;
        }

        var cancelled = order.Cancel(timeProvider.GetUtcNow());
        if (cancelled.IsFailure)
        {
            return cancelled.Error;
        }

        return await db.SaveClosedOrderAndReleaseSeatsAsync(order, ct)
            ? OrderDto.From(order, order.Event.Title)
            : OrderErrors.ConcurrentModification;
    }

    public async Task<Result<OrderDto>> GetAsync(Guid orderId, CancellationToken ct)
    {
        var order = await LoadOwnedOrderAsync(orderId, tracking: false, ct);
        return order is null ? OrderErrors.NotFound : OrderDto.From(order, order.Event.Title);
    }

    public Task<PagedResult<OrderSummaryDto>> GetMineAsync(PageQuery page, CancellationToken ct)
    {
        var userId = currentUser.UserId;
        return db.Orders.AsNoTracking()
            .Where(o => o.UserId == userId)
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => new OrderSummaryDto(
                o.Id, o.EventId, o.Event.Title, o.Status, o.TotalAmount, o.Currency,
                o.Items.Sum(i => i.Quantity), o.CreatedAt, o.ExpiresAt))
            .ToPagedResultAsync(page, ct);
    }

    private async Task<Order?> LoadOwnedOrderAsync(Guid orderId, bool tracking, CancellationToken ct)
    {
        var query = db.Orders
            .Include(o => o.Items)
            .Include(o => o.Tickets)
            .Include(o => o.Event)
            .AsSplitQuery();

        if (!tracking)
        {
            query = query.AsNoTracking();
        }

        var order = await query.FirstOrDefaultAsync(o => o.Id == orderId, ct);

        // Other users' orders are reported as "not found" so that ids cannot be probed.
        return order is not null && (order.UserId == currentUser.UserId || currentUser.IsAdmin) ? order : null;
    }

    private async Task<OrderDto?> FindByIdempotencyKeyAsync(Guid userId, string key, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking()
            .Include(o => o.Items)
            .Include(o => o.Tickets)
            .Include(o => o.Event)
            .AsSplitQuery()
            .FirstOrDefaultAsync(o => o.UserId == userId && o.IdempotencyKey == key, ct);

        return order is null ? null : OrderDto.From(order, order.Event.Title);
    }
}
