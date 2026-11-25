using Microsoft.AspNetCore.Identity;
using TicketFlow.Domain.Users;
using IPasswordHasher = TicketFlow.Application.Abstractions.IPasswordHasher;

namespace TicketFlow.Infrastructure.Security;

/// <summary>
/// Delegates to ASP.NET Core Identity's hasher: PBKDF2-HMAC-SHA512, 100k iterations, random salt,
/// versioned format (old hashes keep verifying when the defaults are strengthened).
/// </summary>
internal sealed class IdentityPasswordHasher : IPasswordHasher
{
    private readonly PasswordHasher<User> _inner = new();

    public string Hash(string password) => _inner.HashPassword(null!, password);

    public bool Verify(string passwordHash, string password) =>
        _inner.VerifyHashedPassword(null!, passwordHash, password) != PasswordVerificationResult.Failed;
}
