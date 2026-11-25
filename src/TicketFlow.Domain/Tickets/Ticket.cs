using System.Security.Cryptography;
using TicketFlow.Domain.Common;
using TicketFlow.Domain.Events;

namespace TicketFlow.Domain.Tickets;

public enum TicketStatus
{
    Valid,
    CheckedIn,
    Cancelled,
}

public sealed class Ticket : AggregateRoot
{
    // Crockford Base32 alphabet: no I, L, O, U, so codes are easy to read out loud at the door.
    private const string CodeAlphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    private Ticket() { }

    public Guid OrderId { get; private set; }
    public Guid EventId { get; private set; }
    public Event Event { get; private set; } = null!;
    public Guid TicketTierId { get; private set; }
    public TicketTier TicketTier { get; private set; } = null!;
    public Guid OwnerId { get; private set; }

    /// <summary>Human-friendly, unguessable code printed on the ticket / encoded in its QR.</summary>
    public string Code { get; private set; } = null!;

    public TicketStatus Status { get; private set; }
    public DateTimeOffset IssuedAt { get; private set; }
    public DateTimeOffset? CheckedInAt { get; private set; }

    internal static Ticket Issue(Guid orderId, Guid eventId, Guid ticketTierId, Guid ownerId, DateTimeOffset now) => new()
    {
        OrderId = orderId,
        EventId = eventId,
        TicketTierId = ticketTierId,
        OwnerId = ownerId,
        Code = GenerateCode(),
        Status = TicketStatus.Valid,
        IssuedAt = now,
    };

    public Result CheckIn(DateTimeOffset now)
    {
        switch (Status)
        {
            case TicketStatus.CheckedIn:
                return TicketErrors.AlreadyCheckedIn(CheckedInAt!.Value);
            case TicketStatus.Cancelled:
                return TicketErrors.Cancelled;
            default:
                Status = TicketStatus.CheckedIn;
                CheckedInAt = now;
                return Result.Success();
        }
    }

    internal void Void() => Status = TicketStatus.Cancelled;

    /// <summary>Format: TF-XXXXX-XXXXX (50 bits of entropy from a CSPRNG).</summary>
    public static string GenerateCode()
    {
        var raw = RandomNumberGenerator.GetString(CodeAlphabet, 10);
        return $"TF-{raw[..5]}-{raw[5..]}";
    }
}
