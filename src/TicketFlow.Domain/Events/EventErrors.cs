using TicketFlow.Domain.Common;

namespace TicketFlow.Domain.Events;

public static class EventErrors
{
    public static readonly Error NotFound = Error.NotFound("Event.NotFound", "The event was not found.");
    public static readonly Error NotOrganizer = Error.Forbidden("Event.NotOrganizer", "Only the organizer of this event can perform this action.");
    public static readonly Error StartsInPast = Error.Validation("Event.StartsInPast", "The event must start in the future.");
    public static readonly Error InvalidSchedule = Error.Validation("Event.InvalidSchedule", "The event must end after it starts.");
    public static readonly Error InvalidTier = Error.Validation("Event.InvalidTier", "A ticket tier needs a non-negative price and a positive capacity.");
    public static readonly Error NotDraft = Error.Conflict("Event.NotDraft", "Only draft events can be published.");
    public static readonly Error NoTicketTiers = Error.Conflict("Event.NoTicketTiers", "An event needs at least one ticket tier before it can be published.");
    public static readonly Error AlreadyCancelled = Error.Conflict("Event.AlreadyCancelled", "The event has been cancelled.");
    public static readonly Error AlreadyFinished = Error.Conflict("Event.AlreadyFinished", "The event has already finished.");
    public static readonly Error NotOnSale = Error.Conflict("Event.NotOnSale", "Tickets for this event are not on sale.");

    public static Error DuplicateTier(string name) =>
        Error.Conflict("Event.DuplicateTier", $"The event already has a ticket tier named '{name}'.");
}
