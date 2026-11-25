using TicketFlow.Domain.Users;

namespace TicketFlow.Application.Abstractions;

public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    /// <summary>Id of the authenticated user. Throws when the request is anonymous.</summary>
    Guid UserId { get; }

    UserRole? Role { get; }

    bool IsAdmin => Role == UserRole.Admin;
}

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string passwordHash, string password);
}

public sealed record AccessToken(string Token, DateTimeOffset ExpiresAt);

public interface ITokenService
{
    AccessToken CreateAccessToken(User user);
    string GenerateRefreshToken();
    string HashRefreshToken(string refreshToken);
}

public sealed record PaymentRequest(Guid OrderId, decimal Amount, string Currency, string CardToken);

public sealed record PaymentResult(bool Succeeded, string? Reference, string? FailureReason)
{
    public static PaymentResult Success(string reference) => new(true, reference, null);
    public static PaymentResult Failure(string reason) => new(false, null, reason);
}

/// <summary>Port to a payment provider (Stripe, iyzico, ...). A fake adapter ships with the project.</summary>
public interface IPaymentGateway
{
    Task<PaymentResult> ChargeAsync(PaymentRequest request, CancellationToken cancellationToken);
    Task RefundAsync(string paymentReference, decimal amount, string currency, CancellationToken cancellationToken);
}
