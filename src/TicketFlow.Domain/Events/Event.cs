using TicketFlow.Domain.Common;

namespace TicketFlow.Domain.Events;

public enum EventStatus
{
    Draft,
    Published,
    Cancelled,
}

public enum EventCategory
{
    Concert,
    Theatre,
    Sports,
    Conference,
    Festival,
    Other,
}

/// <summary>The editable details of an event, grouped so they can be validated together.</summary>
public sealed record EventDetails(
    string Title,
    string Description,
    string Venue,
    string City,
    EventCategory Category,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt);

public sealed class Event : AggregateRoot
{
    private readonly List<TicketTier> _ticketTiers = [];

    private Event() { }

    public Guid OrganizerId { get; private set; }
    public string Title { get; private set; } = null!;
    public string Description { get; private set; } = null!;
    public string Venue { get; private set; } = null!;
    public string City { get; private set; } = null!;
    public EventCategory Category { get; private set; }
    public DateTimeOffset StartsAt { get; private set; }
    public DateTimeOffset EndsAt { get; private set; }
    public EventStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }

    public IReadOnlyCollection<TicketTier> TicketTiers => _ticketTiers;

    public static Result<Event> Create(Guid organizerId, EventDetails details, DateTimeOffset now)
    {
        var schedule = ValidateSchedule(details, now);
        if (schedule.IsFailure)
        {
            return schedule.Error;
        }

        var @event = new Event
        {
            OrganizerId = organizerId,
            Status = EventStatus.Draft,
            CreatedAt = now,
        };
        @event.Apply(details);
        return @event;
    }

    public Result Update(EventDetails details, DateTimeOffset now)
    {
        if (Status == EventStatus.Cancelled)
        {
            return EventErrors.AlreadyCancelled;
        }

        var schedule = ValidateSchedule(details, now);
        if (schedule.IsFailure)
        {
            return schedule;
        }

        Apply(details);
        return Result.Success();
    }

    public Result<TicketTier> AddTicketTier(string name, decimal price, int capacity)
    {
        if (Status == EventStatus.Cancelled)
        {
            return EventErrors.AlreadyCancelled;
        }

        if (price < 0 || capacity <= 0)
        {
            return EventErrors.InvalidTier;
        }

        var trimmed = name.Trim();
        if (_ticketTiers.Exists(t => string.Equals(t.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            return EventErrors.DuplicateTier(trimmed);
        }

        var tier = TicketTier.Create(Id, trimmed, price, capacity);
        _ticketTiers.Add(tier);
        return tier;
    }

    public Result Publish(DateTimeOffset now)
    {
        if (Status != EventStatus.Draft)
        {
            return EventErrors.NotDraft;
        }

        if (_ticketTiers.Count == 0)
        {
            return EventErrors.NoTicketTiers;
        }

        if (StartsAt <= now)
        {
            return EventErrors.StartsInPast;
        }

        Status = EventStatus.Published;
        PublishedAt = now;
        return Result.Success();
    }

    public Result Cancel(DateTimeOffset now)
    {
        if (Status == EventStatus.Cancelled)
        {
            return EventErrors.AlreadyCancelled;
        }

        if (EndsAt <= now)
        {
            return EventErrors.AlreadyFinished;
        }

        Status = EventStatus.Cancelled;
        CancelledAt = now;
        return Result.Success();
    }

    public bool IsOnSale(DateTimeOffset now) => Status == EventStatus.Published && StartsAt > now;

    public bool IsOrganizedBy(Guid userId) => OrganizerId == userId;

    private void Apply(EventDetails details)
    {
        Title = details.Title.Trim();
        Description = details.Description.Trim();
        Venue = details.Venue.Trim();
        City = details.City.Trim();
        Category = details.Category;
        // PostgreSQL 'timestamptz' only accepts UTC offsets; normalise at the boundary of the model.
        StartsAt = details.StartsAt.ToUniversalTime();
        EndsAt = details.EndsAt.ToUniversalTime();
    }

    private static Result ValidateSchedule(EventDetails details, DateTimeOffset now)
    {
        if (details.StartsAt <= now)
        {
            return EventErrors.StartsInPast;
        }

        return details.EndsAt <= details.StartsAt ? EventErrors.InvalidSchedule : Result.Success();
    }
}
