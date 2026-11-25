using FluentValidation;
using TicketFlow.Application.Common;
using TicketFlow.Domain.Users;

namespace TicketFlow.Application.Auth;

public sealed record RegisterRequest(string Email, string Password, string FullName);

public sealed record LoginRequest(string Email, string Password);

public sealed record RefreshTokenRequest(string RefreshToken);

public sealed record UserDto(Guid Id, string Email, string FullName, UserRole Role, DateTimeOffset CreatedAt)
{
    public static UserDto From(User user) => new(user.Id, user.Email, user.FullName, user.Role, user.CreatedAt);
}

public sealed record AuthResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    UserDto User);

internal sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(r => r.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(r => r.Password).StrongPassword();
        RuleFor(r => r.FullName).NotEmpty().Length(2, 100);
    }
}

internal sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(r => r.Email).NotEmpty().MaximumLength(256);
        RuleFor(r => r.Password).NotEmpty().MaximumLength(128);
    }
}

internal sealed class RefreshTokenRequestValidator : AbstractValidator<RefreshTokenRequest>
{
    public RefreshTokenRequestValidator() => RuleFor(r => r.RefreshToken).NotEmpty().MaximumLength(200);
}
