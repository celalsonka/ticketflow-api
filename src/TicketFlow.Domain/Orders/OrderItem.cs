using TicketFlow.Domain.Common;
using TicketFlow.Domain.Events;

namespace TicketFlow.Domain.Orders;

/// <summary>
/// One line of an order. Tier name and price are snapshotted so the order keeps
/// describing what the customer actually paid even if the tier changes later.
/// </summary>
public sealed class OrderItem : Entity
{
    private OrderItem() { }

    public Guid OrderId { get; private set; }
    public Guid TicketTierId { get; private set; }
    public string TierName { get; private set; } = null!;
    public decimal UnitPrice { get; private set; }
    public int Quantity { get; private set; }

    public decimal LineTotal => UnitPrice * Quantity;

    internal static OrderItem Create(Guid orderId, TicketTier tier, int quantity) => new()
    {
        OrderId = orderId,
        TicketTierId = tier.Id,
        TierName = tier.Name,
        UnitPrice = tier.Price,
        Quantity = quantity,
    };
}
