using TicketFlow.Domain.Common;

namespace TicketFlow.Domain.Events;

/// <summary>
/// A price category of an event (e.g. "VIP", "General Admission").
/// <see cref="Sold"/> is never incremented in memory: it is changed only by an atomic,
/// conditional SQL UPDATE so that concurrent buyers can never oversell the tier.
/// </summary>
public sealed class TicketTier : Entity
{
    private TicketTier() { }

    public Guid EventId { get; private set; }
    public string Name { get; private set; } = null!;
    public decimal Price { get; private set; }
    public int Capacity { get; private set; }

    /// <summary>Seats held by pending orders plus seats of paid orders.</summary>
    public int Sold { get; private set; }

    public int Available => Capacity - Sold;

    internal static TicketTier Create(Guid eventId, string name, decimal price, int capacity) => new()
    {
        EventId = eventId,
        Name = name,
        Price = price,
        Capacity = capacity,
    };
}
