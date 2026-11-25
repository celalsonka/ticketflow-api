using TicketFlow.Domain.Common;
using TicketFlow.Domain.Events;
using TicketFlow.Domain.Tickets;

namespace TicketFlow.Domain.Orders;

public enum OrderStatus
{
    /// <summary>Seats are held; the order must be paid before <see cref="Order.ExpiresAt"/>.</summary>
    Pending,
    Paid,
    Cancelled,
    Expired,
    Refunded,
}

public sealed class Order : AggregateRoot
{
    private readonly List<OrderItem> _items = [];
    private readonly List<Ticket> _tickets = [];

    private Order() { }

    public Guid UserId { get; private set; }
    public Guid EventId { get; private set; }
    public Event Event { get; private set; } = null!;
    public OrderStatus Status { get; private set; }
    public decimal TotalAmount { get; private set; }
    public string Currency { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? PaidAt { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }
    public string? PaymentReference { get; private set; }

    /// <summary>Client-supplied key that makes order creation safe to retry.</summary>
    public string? IdempotencyKey { get; private set; }

    public IReadOnlyCollection<OrderItem> Items => _items;
    public IReadOnlyCollection<Ticket> Tickets => _tickets;

    public static Result<Order> Create(
        Guid userId,
        Guid eventId,
        IReadOnlyCollection<(TicketTier Tier, int Quantity)> lines,
        string currency,
        DateTimeOffset now,
        TimeSpan reservationHold,
        string? idempotencyKey)
    {
        if (lines.Count == 0)
        {
            return OrderErrors.Empty;
        }

        var order = new Order
        {
            UserId = userId,
            EventId = eventId,
            Status = OrderStatus.Pending,
            Currency = currency,
            CreatedAt = now,
            ExpiresAt = now.Add(reservationHold),
            IdempotencyKey = idempotencyKey,
        };

        foreach (var (tier, quantity) in lines)
        {
            if (quantity <= 0)
            {
                return OrderErrors.InvalidQuantity;
            }

            if (tier.EventId != eventId)
            {
                return OrderErrors.TierNotInEvent;
            }

            order._items.Add(OrderItem.Create(order.Id, tier, quantity));
        }

        order.TotalAmount = order._items.Sum(i => i.LineTotal);
        return order;
    }

    public bool IsExpired(DateTimeOffset now) => Status == OrderStatus.Pending && ExpiresAt <= now;

    /// <summary>Confirms payment and issues one ticket per purchased seat.</summary>
    public Result MarkPaid(string paymentReference, DateTimeOffset now)
    {
        if (Status != OrderStatus.Pending)
        {
            return OrderErrors.NotPending(Status);
        }

        if (ExpiresAt <= now)
        {
            return OrderErrors.ReservationExpired;
        }

        Status = OrderStatus.Paid;
        PaidAt = now;
        PaymentReference = paymentReference;

        foreach (var item in _items)
        {
            for (var i = 0; i < item.Quantity; i++)
            {
                _tickets.Add(Ticket.Issue(Id, EventId, item.TicketTierId, UserId, now));
            }
        }

        return Result.Success();
    }

    /// <summary>Customer gives up a pending reservation. Caller must release the held seats.</summary>
    public Result Cancel(DateTimeOffset now)
    {
        if (Status != OrderStatus.Pending)
        {
            return OrderErrors.NotPending(Status);
        }

        Status = OrderStatus.Cancelled;
        ClosedAt = now;
        return Result.Success();
    }

    /// <summary>Reservation window elapsed without payment. Caller must release the held seats.</summary>
    public Result Expire(DateTimeOffset now)
    {
        if (Status != OrderStatus.Pending)
        {
            return OrderErrors.NotPending(Status);
        }

        if (ExpiresAt > now)
        {
            return OrderErrors.NotYetExpired;
        }

        Status = OrderStatus.Expired;
        ClosedAt = now;
        return Result.Success();
    }

    /// <summary>The organizer cancelled the event: pending orders are voided, paid ones refunded.</summary>
    public void CloseBecauseEventCancelled(DateTimeOffset now)
    {
        switch (Status)
        {
            case OrderStatus.Pending:
                Status = OrderStatus.Cancelled;
                ClosedAt = now;
                break;
            case OrderStatus.Paid:
                Status = OrderStatus.Refunded;
                ClosedAt = now;
                foreach (var ticket in _tickets)
                {
                    ticket.Void();
                }

                break;
        }
    }
}
