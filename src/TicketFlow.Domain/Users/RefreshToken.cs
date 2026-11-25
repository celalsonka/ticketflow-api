using TicketFlow.Domain.Common;

namespace TicketFlow.Domain.Users;

/// <summary>
/// Long-lived credential used to obtain new access tokens. Only a SHA-256 hash is stored,
/// so a database leak does not leak usable tokens. Tokens are rotated on every use.
/// </summary>
public sealed class RefreshToken : AggregateRoot
{
    private RefreshToken() { }

    public Guid UserId { get; private set; }
    public User User { get; private set; } = null!;
    public string TokenHash { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public string? RevokedReason { get; private set; }
    public string? ReplacedByTokenHash { get; private set; }

    public bool IsRevoked => RevokedAt is not null;

    public static RefreshToken Issue(Guid userId, string tokenHash, DateTimeOffset now, TimeSpan lifetime) => new()
    {
        UserId = userId,
        TokenHash = tokenHash,
        CreatedAt = now,
        ExpiresAt = now.Add(lifetime),
    };

    public bool IsActive(DateTimeOffset now) => !IsRevoked && ExpiresAt > now;

    public void Revoke(DateTimeOffset now, string reason, string? replacedByTokenHash = null)
    {
        if (IsRevoked)
        {
            return;
        }

        RevokedAt = now;
        RevokedReason = reason;
        ReplacedByTokenHash = replacedByTokenHash;
    }
}
