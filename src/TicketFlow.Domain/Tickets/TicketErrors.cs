using TicketFlow.Domain.Common;

namespace TicketFlow.Domain.Tickets;

public static class TicketErrors
{
    public static readonly Error NotFound = Error.NotFound("Ticket.NotFound", "The ticket was not found.");
    public static readonly Error Cancelled = Error.Conflict("Ticket.Cancelled", "The ticket has been cancelled and is not valid for entry.");

    public static Error AlreadyCheckedIn(DateTimeOffset at) =>
        Error.Conflict("Ticket.AlreadyCheckedIn", $"The ticket was already used for entry at {at:u}.");
}
