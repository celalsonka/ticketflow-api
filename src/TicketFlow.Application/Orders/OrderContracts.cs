using FluentValidation;
using TicketFlow.Domain.Orders;
using TicketFlow.Domain.Tickets;

namespace TicketFlow.Application.Orders;

public sealed record OrderItemRequest(Guid TicketTierId, int Quantity);

public sealed record CreateOrderRequest(Guid EventId, IReadOnlyList<OrderItemRequest> Items);

/// <summary>
/// Token produced by the payment provider's client SDK (card data never touches this API).
/// Test tokens: any value succeeds except <c>tok_declined</c> and <c>tok_insufficient_funds</c>.
/// </summary>
public sealed record PayOrderRequest(string CardToken);

public sealed record OrderItemDto(Guid TicketTierId, string TierName, decimal UnitPrice, int Quantity, decimal LineTotal);

public sealed record OrderTicketDto(Guid Id, string Code, Guid TicketTierId, TicketStatus Status);

public sealed record OrderDto(
    Guid Id,
    Guid EventId,
    string EventTitle,
    OrderStatus Status,
    decimal TotalAmount,
    string Currency,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? PaidAt,
    IReadOnlyList<OrderItemDto> Items,
    IReadOnlyList<OrderTicketDto> Tickets)
{
    public static OrderDto From(Order order, string eventTitle) => new(
        order.Id,
        order.EventId,
        eventTitle,
        order.Status,
        order.TotalAmount,
        order.Currency,
        order.CreatedAt,
        order.ExpiresAt,
        order.PaidAt,
        order.Items.Select(i => new OrderItemDto(i.TicketTierId, i.TierName, i.UnitPrice, i.Quantity, i.LineTotal)).ToList(),
        order.Tickets.OrderBy(t => t.Code).Select(t => new OrderTicketDto(t.Id, t.Code, t.TicketTierId, t.Status)).ToList());
}

public sealed record OrderSummaryDto(
    Guid Id,
    Guid EventId,
    string EventTitle,
    OrderStatus Status,
    decimal TotalAmount,
    string Currency,
    int TicketCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);

/// <param name="Order">The placed order.</param>
/// <param name="Replayed">True when an earlier order with the same Idempotency-Key was returned instead of creating a new one.</param>
public sealed record OrderPlacement(OrderDto Order, bool Replayed);

internal sealed class CreateOrderRequestValidator : AbstractValidator<CreateOrderRequest>
{
    public CreateOrderRequestValidator()
    {
        RuleFor(o => o.EventId).NotEmpty();
        RuleFor(o => o.Items).NotEmpty();
        RuleForEach(o => o.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.TicketTierId).NotEmpty();
            item.RuleFor(i => i.Quantity).InclusiveBetween(1, 100);
        });
    }
}

internal sealed class PayOrderRequestValidator : AbstractValidator<PayOrderRequest>
{
    public PayOrderRequestValidator() => RuleFor(p => p.CardToken).NotEmpty().MaximumLength(100);
}
