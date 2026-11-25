using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using TicketFlow.Api.Common;
using TicketFlow.Application.Common;
using TicketFlow.Application.Events;
using TicketFlow.Domain.Common;

namespace TicketFlow.Api.Controllers;

/// <summary>Public event catalogue and organizer event management.</summary>
[Route("api/v1/events")]
public sealed class EventsController(EventService events, IOutputCacheStore cache) : ApiControllerBase
{
    /// <summary>Searches published, upcoming events.</summary>
    /// <remarks>Cached for 15 s and evicted when events change. Seat availability shown here is indicative;
    /// the authoritative check happens atomically when an order is placed.</remarks>
    [AllowAnonymous]
    [HttpGet]
    [OutputCache(PolicyName = CacheTags.Events)]
    [ProducesResponseType<PagedResult<EventSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<EventSummaryDto>>> Search([FromQuery] EventQuery query, CancellationToken ct) =>
        Ok(await events.SearchAsync(query, ct));

    /// <summary>Event details with ticket tiers and live availability.</summary>
    [AllowAnonymous]
    [HttpGet("{id:guid}")]
    [ProducesResponseType<EventDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EventDto>> GetById(Guid id, CancellationToken ct) =>
        OkOrProblem(await events.GetByIdAsync(id, ct));

    /// <summary>Events managed by the caller, in every status (admins see all).</summary>
    [Authorize(Policy = Policies.Organizer)]
    [HttpGet("managed")]
    [ProducesResponseType<PagedResult<EventSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<EventSummaryDto>>> Managed([FromQuery] PageQuery page, CancellationToken ct) =>
        Ok(await events.GetManagedAsync(page, ct));

    /// <summary>Creates a draft event with its ticket tiers.</summary>
    [Authorize(Policy = Policies.Organizer)]
    [HttpPost]
    [ProducesResponseType<EventDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<EventDto>> Create(CreateEventRequest request, CancellationToken ct)
    {
        var result = await events.CreateAsync(request, ct);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetById), new { id = result.Value.Id }, result.Value)
            : Problem(result.Error);
    }

    [Authorize(Policy = Policies.Organizer)]
    [HttpPut("{id:guid}")]
    [ProducesResponseType<EventDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<EventDto>> Update(Guid id, UpdateEventRequest request, CancellationToken ct) =>
        await EvictOnSuccess(await events.UpdateAsync(id, request, ct), ct);

    [Authorize(Policy = Policies.Organizer)]
    [HttpPost("{id:guid}/ticket-tiers")]
    [ProducesResponseType<TicketTierDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TicketTierDto>> AddTicketTier(Guid id, TicketTierRequest request, CancellationToken ct)
    {
        var result = await events.AddTicketTierAsync(id, request, ct);
        if (result.IsFailure)
        {
            return Problem(result.Error);
        }

        await cache.EvictByTagAsync(CacheTags.Events, ct);
        return CreatedAtAction(nameof(GetById), new { id }, result.Value);
    }

    /// <summary>Puts a draft event on sale.</summary>
    [Authorize(Policy = Policies.Organizer)]
    [HttpPost("{id:guid}/publish")]
    [ProducesResponseType<EventDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<EventDto>> Publish(Guid id, CancellationToken ct) =>
        await EvictOnSuccess(await events.PublishAsync(id, ct), ct);

    /// <summary>Cancels the event: pending reservations are voided and paid orders refunded.</summary>
    [Authorize(Policy = Policies.Organizer)]
    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType<EventDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<EventDto>> Cancel(Guid id, CancellationToken ct) =>
        await EvictOnSuccess(await events.CancelAsync(id, ct), ct);

    /// <summary>Sales, reservations, check-ins and revenue per ticket tier.</summary>
    [Authorize(Policy = Policies.Organizer)]
    [HttpGet("{id:guid}/sales-report")]
    [ProducesResponseType<EventSalesReport>(StatusCodes.Status200OK)]
    public async Task<ActionResult<EventSalesReport>> SalesReport(Guid id, CancellationToken ct) =>
        OkOrProblem(await events.GetSalesReportAsync(id, ct));

    private async Task<ActionResult<T>> EvictOnSuccess<T>(Result<T> result, CancellationToken ct)
    {
        if (result.IsSuccess)
        {
            await cache.EvictByTagAsync(CacheTags.Events, ct);
        }

        return OkOrProblem(result);
    }
}
