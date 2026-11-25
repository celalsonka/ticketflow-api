using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using TicketFlow.Application.Abstractions;
using TicketFlow.Domain.Users;

namespace TicketFlow.Api.Common;

internal sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public Guid UserId => Guid.TryParse(Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub), out var id)
        ? id
        : throw new InvalidOperationException("The current request is not authenticated.");

    public UserRole? Role => Enum.TryParse<UserRole>(Principal?.FindFirstValue(Policies.RoleClaim), out var role) ? role : null;
}
