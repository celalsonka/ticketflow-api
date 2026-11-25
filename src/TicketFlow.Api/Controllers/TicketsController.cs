using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TicketFlow.Api.Common;
using TicketFlow.Application.Common;
using TicketFlow.Application.Tickets;

namespace TicketFlow.Api.Controllers;

/// <summary>Issued tickets and venue check-in.</summary>
[Authorize]
[Route("api/v1/tickets")]
public sealed class TicketsController(TicketService tickets) : ApiControllerBase
{
    /// <summary>The caller's tickets, soonest event first.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<TicketDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<TicketDto>>> Mine([FromQuery] PageQuery page, CancellationToken ct) =>
        Ok(await tickets.GetMineAsync(page, ct));

    /// <summary>Looks up a ticket by its code (owner, event organizer or admin).</summary>
    [HttpGet("{code}")]
    [ProducesResponseType<TicketDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TicketDto>> GetByCode(string code, CancellationToken ct) =>
        OkOrProblem(await tickets.GetByCodeAsync(code, ct));

    /// <summary>Admits a ticket holder at the venue. A ticket can be checked in only once.</summary>
    [Authorize(Policy = Policies.Organizer)]
    [HttpPost("check-in")]
    [ProducesResponseType<CheckInResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CheckInResponse>> CheckIn(CheckInRequest request, CancellationToken ct) =>
        OkOrProblem(await tickets.CheckInAsync(request, ct));
}
