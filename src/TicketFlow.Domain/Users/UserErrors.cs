using TicketFlow.Domain.Common;

namespace TicketFlow.Domain.Users;

public static class UserErrors
{
    public static readonly Error NotFound = Error.NotFound("User.NotFound", "The user was not found.");
    public static readonly Error EmailTaken = Error.Conflict("User.EmailTaken", "An account with this email already exists.");
    public static readonly Error InvalidCredentials = Error.Unauthorized("Auth.InvalidCredentials", "Email or password is incorrect.");
    public static readonly Error InvalidRefreshToken = Error.Unauthorized("Auth.InvalidRefreshToken", "The refresh token is invalid, expired or has been revoked.");
    public static readonly Error CannotChangeOwnRole = Error.Conflict("User.CannotChangeOwnRole", "Administrators cannot change their own role.");
}
