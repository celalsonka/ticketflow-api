using TicketFlow.Domain.Events;
using TicketFlow.Domain.Orders;
using TicketFlow.Domain.Tickets;

namespace TicketFlow.UnitTests.Domain;

public class OrderTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Hold = TimeSpan.FromMinutes(10);

    private static (Event Event, TicketTier Standard, TicketTier Vip) PublishedEvent()
    {
        var @event = Event.Create(Guid.NewGuid(), EventTests.Details(), Now).Value;
        var standard = @event.AddTicketTier("Standard", 100m, 100).Value;
        var vip = @event.AddTicketTier("VIP", 250.50m, 10).Value;
        @event.Publish(Now);
        return (@event, standard, vip);
    }

    private static Order PendingOrder(int standard = 2, int vip = 1)
    {
        var (@event, standardTier, vipTier) = PublishedEvent();
        return Order.Create(Guid.NewGuid(), @event.Id, [(standardTier, standard), (vipTier, vip)], "TRY", Now, Hold, null).Value;
    }

    [Fact]
    public void Create_snapshots_prices_and_computes_total()
    {
        var order = PendingOrder(standard: 2, vip: 1);

        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal(450.50m, order.TotalAmount);
        Assert.Equal(Now + Hold, order.ExpiresAt);
        Assert.Contains(order.Items, i => i is { TierName: "VIP", UnitPrice: 250.50m, Quantity: 1 });
    }

    [Fact]
    public void Create_rejects_tiers_from_another_event()
    {
        var (_, foreignTier, _) = PublishedEvent();
        var (@event, _, _) = PublishedEvent();

        var result = Order.Create(Guid.NewGuid(), @event.Id, [(foreignTier, 1)], "TRY", Now, Hold, null);

        Assert.Equal(OrderErrors.TierNotInEvent, result.Error);
    }

    [Fact]
    public void Create_rejects_empty_and_non_positive_quantities()
    {
        var (@event, tier, _) = PublishedEvent();

        Assert.Equal(OrderErrors.Empty, Order.Create(Guid.NewGuid(), @event.Id, [], "TRY", Now, Hold, null).Error);
        Assert.Equal(OrderErrors.InvalidQuantity, Order.Create(Guid.NewGuid(), @event.Id, [(tier, 0)], "TRY", Now, Hold, null).Error);
    }

    [Fact]
    public void MarkPaid_issues_one_unique_ticket_per_seat()
    {
        var order = PendingOrder(standard: 3, vip: 2);

        var result = order.MarkPaid("pay_123", Now.AddMinutes(1));

        Assert.True(result.IsSuccess);
        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Equal(5, order.Tickets.Count);
        Assert.Equal(5, order.Tickets.Select(t => t.Code).Distinct().Count());
        Assert.All(order.Tickets, t => Assert.Equal(TicketStatus.Valid, t.Status));
    }

    [Fact]
    public void MarkPaid_after_reservation_expired_is_rejected()
    {
        var order = PendingOrder();

        var result = order.MarkPaid("pay_123", Now + Hold);

        Assert.Equal(OrderErrors.ReservationExpired, result.Error);
        Assert.Empty(order.Tickets);
    }

    [Fact]
    public void Paid_order_cannot_be_paid_cancelled_or_expired()
    {
        var order = PendingOrder();
        order.MarkPaid("pay_123", Now);

        Assert.Equal("Order.NotPending", order.MarkPaid("pay_456", Now).Error.Code);
        Assert.Equal("Order.NotPending", order.Cancel(Now).Error.Code);
        Assert.Equal("Order.NotPending", order.Expire(Now.AddDays(1)).Error.Code);
    }

    [Fact]
    public void Expire_only_after_the_hold_window()
    {
        var order = PendingOrder();

        Assert.Equal(OrderErrors.NotYetExpired, order.Expire(Now.AddMinutes(9)).Error);
        Assert.True(order.IsExpired(Now + Hold));
        Assert.True(order.Expire(Now + Hold).IsSuccess);
        Assert.Equal(OrderStatus.Expired, order.Status);
    }

    [Fact]
    public void Event_cancellation_refunds_paid_orders_and_voids_their_tickets()
    {
        var paid = PendingOrder();
        paid.MarkPaid("pay_123", Now);
        var pending = PendingOrder();

        paid.CloseBecauseEventCancelled(Now);
        pending.CloseBecauseEventCancelled(Now);

        Assert.Equal(OrderStatus.Refunded, paid.Status);
        Assert.All(paid.Tickets, t => Assert.Equal(TicketStatus.Cancelled, t.Status));
        Assert.Equal(OrderStatus.Cancelled, pending.Status);
    }
}
