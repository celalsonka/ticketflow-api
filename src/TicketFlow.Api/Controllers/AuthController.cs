using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TicketFlow.Api.Common;
using TicketFlow.Application.Abstractions;
using TicketFlow.Application.Auth;

namespace TicketFlow.Api.Controllers;

/// <summary>Registration, login and token lifecycle.</summary>
[Route("api/v1/auth")]
[EnableRateLimiting(RateLimitPolicies.Auth)]
public sealed class AuthController(AuthService auth, ICurrentUser currentUser) : ApiControllerBase
{
    /// <summary>Creates a customer account and signs it in.</summary>
    [HttpPost("register")]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request, CancellationToken ct)
    {
        var result = await auth.RegisterAsync(request, ct);
        return result.IsSuccess ? CreatedAtAction(nameof(Me), null, result.Value) : Problem(result.Error);
    }

    /// <summary>Exchanges credentials for a short-lived access token and a refresh token.</summary>
    [HttpPost("login")]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken ct) =>
        OkOrProblem(await auth.LoginAsync(request, ct));

    /// <summary>Rotates a refresh token. Each refresh token can be used once; reuse revokes all sessions.</summary>
    [HttpPost("refresh")]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponse>> Refresh(RefreshTokenRequest request, CancellationToken ct) =>
        OkOrProblem(await auth.RefreshAsync(request, ct));

    /// <summary>Revokes a refresh token (sign out from one device).</summary>
    [Authorize]
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(RefreshTokenRequest request, CancellationToken ct)
    {
        var result = await auth.LogoutAsync(currentUser.UserId, request, ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error);
    }

    /// <summary>Profile of the signed-in user.</summary>
    [Authorize]
    [DisableRateLimiting]
    [HttpGet("me")]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<UserDto>> Me(CancellationToken ct) =>
        OkOrProblem(await auth.GetProfileAsync(currentUser.UserId, ct));
}
