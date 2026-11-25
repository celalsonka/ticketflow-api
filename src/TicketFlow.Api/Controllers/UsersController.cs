using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TicketFlow.Api.Common;
using TicketFlow.Application.Auth;
using TicketFlow.Application.Common;
using TicketFlow.Application.Users;

namespace TicketFlow.Api.Controllers;

/// <summary>User administration.</summary>
[Authorize(Policy = Policies.Admin)]
[Route("api/v1/users")]
public sealed class UsersController(UserService users) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<UserDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<UserDto>>> List([FromQuery] UserQuery query, CancellationToken ct) =>
        Ok(await users.ListAsync(query, ct));

    /// <summary>Grants a role (e.g. promotes a customer to organizer). Takes effect at the next token refresh.</summary>
    [HttpPut("{id:guid}/role")]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserDto>> ChangeRole(Guid id, ChangeRoleRequest request, CancellationToken ct) =>
        OkOrProblem(await users.ChangeRoleAsync(id, request, ct));
}
