using TicketFlow.Domain.Common;

namespace TicketFlow.Domain.Users;

public enum UserRole
{
    Customer,
    Organizer,
    Admin,
}

public sealed class User : AggregateRoot
{
    private User() { }

    public string Email { get; private set; } = null!;
    public string FullName { get; private set; } = null!;
    public string PasswordHash { get; private set; } = null!;
    public UserRole Role { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static User Create(string email, string fullName, string passwordHash, UserRole role, DateTimeOffset now) => new()
    {
        Email = NormalizeEmail(email),
        FullName = fullName.Trim(),
        PasswordHash = passwordHash,
        Role = role,
        CreatedAt = now,
    };

    public void ChangeRole(UserRole role) => Role = role;

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}
