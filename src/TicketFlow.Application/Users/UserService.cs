using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TicketFlow.Application.Abstractions;
using TicketFlow.Application.Auth;
using TicketFlow.Application.Common;
using TicketFlow.Domain.Common;
using TicketFlow.Domain.Users;

namespace TicketFlow.Application.Users;

public sealed class UserQuery : PageQuery
{
    /// <summary>Matches email or full name.</summary>
    public string? Search { get; set; }

    public UserRole? Role { get; set; }
}

public sealed record ChangeRoleRequest(UserRole Role);

internal sealed class UserQueryValidator : AbstractValidator<UserQuery>
{
    public UserQueryValidator()
    {
        this.AddPagingRules();
        RuleFor(q => q.Search).MaximumLength(100);
        RuleFor(q => q.Role).IsInEnum().When(q => q.Role.HasValue);
    }
}

internal sealed class ChangeRoleRequestValidator : AbstractValidator<ChangeRoleRequest>
{
    public ChangeRoleRequestValidator() => RuleFor(r => r.Role).IsInEnum();
}

public sealed class UserService(IAppDbContext db, ICurrentUser currentUser, ILogger<UserService> logger)
{
    public Task<PagedResult<UserDto>> ListAsync(UserQuery query, CancellationToken ct)
    {
        var users = db.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLower();
            users = users.Where(u => u.Email.Contains(term) || u.FullName.ToLower().Contains(term));
        }

        if (query.Role is { } role)
        {
            users = users.Where(u => u.Role == role);
        }

        return users
            .OrderBy(u => u.Email)
            .Select(u => new UserDto(u.Id, u.Email, u.FullName, u.Role, u.CreatedAt))
            .ToPagedResultAsync(query, ct);
    }

    public async Task<Result<UserDto>> ChangeRoleAsync(Guid userId, ChangeRoleRequest request, CancellationToken ct)
    {
        if (userId == currentUser.UserId)
        {
            return UserErrors.CannotChangeOwnRole;
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
        {
            return UserErrors.NotFound;
        }

        user.ChangeRole(request.Role);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("User {UserId} role changed to {Role} by {AdminId}", user.Id, request.Role, currentUser.UserId);
        return UserDto.From(user);
    }
}
