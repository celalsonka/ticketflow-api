using TicketFlow.Domain.Common;

namespace TicketFlow.Domain.Orders;

public static class OrderErrors
{
    public static readonly Error NotFound = Error.NotFound("Order.NotFound", "The order was not found.");
    public static readonly Error Empty = Error.Validation("Order.Empty", "An order must contain at least one item.");
    public static readonly Error InvalidQuantity = Error.Validation("Order.InvalidQuantity", "Quantities must be positive.");
    public static readonly Error TierNotInEvent = Error.Validation("Order.TierNotInEvent", "One of the ticket tiers does not belong to this event.");
    public static readonly Error ReservationExpired = Error.Conflict("Order.ReservationExpired", "The reservation has expired. Please place a new order.");
    public static readonly Error NotYetExpired = Error.Conflict("Order.NotYetExpired", "The reservation has not expired yet.");
    public static readonly Error ConcurrentModification = Error.Conflict("Order.ConcurrentModification", "The order was modified by another request. Please reload it and retry.");

    public static Error SoldOut(string tierName) =>
        Error.Conflict("Order.SoldOut", $"Not enough tickets left in tier '{tierName}'.");

    public static Error TooManyTickets(int max) =>
        Error.Validation("Order.TooManyTickets", $"A single order can contain at most {max} tickets.");

    public static Error NotPending(OrderStatus status) =>
        Error.Conflict("Order.NotPending", $"The order is {status.ToString().ToLowerInvariant()} and can no longer be changed.");

    public static Error PaymentFailed(string reason) =>
        Error.PaymentRequired("Order.PaymentFailed", $"Payment was not completed: {reason}");
}
