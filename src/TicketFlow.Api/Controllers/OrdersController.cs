using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TicketFlow.Api.Common;
using TicketFlow.Application.Common;
using TicketFlow.Application.Orders;
using TicketFlow.Domain.Common;

namespace TicketFlow.Api.Controllers;

/// <summary>Seat reservation, payment and order history.</summary>
[Authorize]
[Route("api/v1/orders")]
public sealed class OrdersController(OrderService orders) : ApiControllerBase
{
    private const string IdempotencyHeader = "Idempotency-Key";
    private const string ReplayedHeader = "Idempotent-Replayed";

    /// <summary>Reserves seats and creates a pending order that must be paid before it expires.</summary>
    /// <param name="request">Event and quantities per ticket tier.</param>
    /// <param name="idempotencyKey">Optional client-generated key (e.g. a UUID). Retrying with the same key
    /// returns the original order instead of reserving seats twice.</param>
    /// <param name="ct">Request cancellation.</param>
    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.Orders)]
    [ProducesResponseType<OrderDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<OrderDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<OrderDto>> Place(
        CreateOrderRequest request,
        [FromHeader(Name = IdempotencyHeader)] string? idempotencyKey,
        CancellationToken ct)
    {
        if (idempotencyKey is { Length: 0 or > 100 })
        {
            return Problem(Error.Validation("Order.InvalidIdempotencyKey", $"{IdempotencyHeader} must be 1-100 characters."));
        }

        var result = await orders.PlaceAsync(request, idempotencyKey, ct);
        if (result.IsFailure)
        {
            return Problem(result.Error);
        }

        var (order, replayed) = result.Value;
        if (replayed)
        {
            Response.Headers[ReplayedHeader] = "true";
            return Ok(order);
        }

        return CreatedAtAction(nameof(GetById), new { id = order.Id }, order);
    }

    /// <summary>Pays a pending order and issues its tickets.</summary>
    /// <remarks>Test card tokens: anything succeeds except <c>tok_declined</c> and <c>tok_insufficient_funds</c>.</remarks>
    [HttpPost("{id:guid}/pay")]
    [ProducesResponseType<OrderDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status402PaymentRequired)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrderDto>> Pay(Guid id, PayOrderRequest request, CancellationToken ct) =>
        OkOrProblem(await orders.PayAsync(id, request, ct));

    /// <summary>Cancels a pending order and releases its seats.</summary>
    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType<OrderDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrderDto>> Cancel(Guid id, CancellationToken ct) =>
        OkOrProblem(await orders.CancelAsync(id, ct));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<OrderDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderDto>> GetById(Guid id, CancellationToken ct) =>
        OkOrProblem(await orders.GetAsync(id, ct));

    /// <summary>The caller's orders, newest first.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<OrderSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<OrderSummaryDto>>> Mine([FromQuery] PageQuery page, CancellationToken ct) =>
        Ok(await orders.GetMineAsync(page, ct));
}
