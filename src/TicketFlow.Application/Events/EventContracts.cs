using FluentValidation;
using TicketFlow.Application.Common;
using TicketFlow.Domain.Events;

namespace TicketFlow.Application.Events;

public sealed record TicketTierRequest(string Name, decimal Price, int Capacity);

public sealed record CreateEventRequest(
    string Title,
    string Description,
    string Venue,
    string City,
    EventCategory Category,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    IReadOnlyList<TicketTierRequest> TicketTiers);

public sealed record UpdateEventRequest(
    string Title,
    string Description,
    string Venue,
    string City,
    EventCategory Category,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt);

public sealed record TicketTierDto(Guid Id, string Name, decimal Price, int Capacity, int Sold, int Available);

public sealed record EventDto(
    Guid Id,
    Guid OrganizerId,
    string Title,
    string Description,
    string Venue,
    string City,
    EventCategory Category,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    EventStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PublishedAt,
    IReadOnlyList<TicketTierDto> TicketTiers)
{
    public static EventDto From(Event e) => new(
        e.Id, e.OrganizerId, e.Title, e.Description, e.Venue, e.City, e.Category,
        e.StartsAt, e.EndsAt, e.Status, e.CreatedAt, e.PublishedAt,
        e.TicketTiers
            .OrderBy(t => t.Price)
            .Select(t => new TicketTierDto(t.Id, t.Name, t.Price, t.Capacity, t.Sold, t.Available))
            .ToList());
}

public sealed record EventSummaryDto(
    Guid Id,
    string Title,
    string Venue,
    string City,
    EventCategory Category,
    DateTimeOffset StartsAt,
    EventStatus Status,
    decimal? MinPrice,
    int AvailableTickets);

public enum EventSortBy
{
    StartsAt,
    Price,
    Title,
}

public sealed class EventQuery : PageQuery
{
    /// <summary>Free-text search in the title.</summary>
    public string? Search { get; set; }

    public string? City { get; set; }
    public EventCategory? Category { get; set; }

    /// <summary>Only events starting at or after this instant (defaults to now).</summary>
    public DateTimeOffset? From { get; set; }

    /// <summary>Only events starting before this instant.</summary>
    public DateTimeOffset? To { get; set; }

    public EventSortBy SortBy { get; set; } = EventSortBy.StartsAt;
    public bool Descending { get; set; }
}

public sealed record TierSalesDto(Guid TierId, string Name, decimal Price, int Capacity, int TicketsSold, int TicketsReserved, int TicketsCheckedIn, decimal Revenue);

public sealed record EventSalesReport(
    Guid EventId,
    string Title,
    EventStatus Status,
    int TotalCapacity,
    int TicketsSold,
    int TicketsReserved,
    int TicketsCheckedIn,
    decimal Revenue,
    string Currency,
    double SellThroughRate,
    IReadOnlyList<TierSalesDto> Tiers);

internal sealed class TicketTierRequestValidator : AbstractValidator<TicketTierRequest>
{
    public TicketTierRequestValidator()
    {
        RuleFor(t => t.Name).NotEmpty().MaximumLength(100);
        RuleFor(t => t.Price).GreaterThanOrEqualTo(0).LessThan(1_000_000).PrecisionScale(10, 2, ignoreTrailingZeros: true);
        RuleFor(t => t.Capacity).InclusiveBetween(1, 100_000);
    }
}

internal sealed class CreateEventRequestValidator : AbstractValidator<CreateEventRequest>
{
    public CreateEventRequestValidator()
    {
        RuleFor(e => e.Title).NotEmpty().MaximumLength(200);
        RuleFor(e => e.Description).NotNull().MaximumLength(4000);
        RuleFor(e => e.Venue).NotEmpty().MaximumLength(200);
        RuleFor(e => e.City).NotEmpty().MaximumLength(100);
        RuleFor(e => e.Category).IsInEnum();
        RuleFor(e => e.EndsAt).GreaterThan(e => e.StartsAt).WithMessage("The event must end after it starts.");
        RuleFor(e => e.TicketTiers).NotNull();
        RuleFor(e => e.TicketTiers.Count).LessThanOrEqualTo(20).When(e => e.TicketTiers is not null).OverridePropertyName("TicketTiers");
        RuleForEach(e => e.TicketTiers).SetValidator(new TicketTierRequestValidator());
        RuleFor(e => e.TicketTiers)
            .Must(tiers => tiers.Select(t => t.Name.Trim().ToUpperInvariant()).Distinct().Count() == tiers.Count)
            .When(e => e.TicketTiers is not null && e.TicketTiers.All(t => t.Name is not null))
            .WithMessage("Ticket tier names must be unique.");
    }
}

internal sealed class UpdateEventRequestValidator : AbstractValidator<UpdateEventRequest>
{
    public UpdateEventRequestValidator()
    {
        RuleFor(e => e.Title).NotEmpty().MaximumLength(200);
        RuleFor(e => e.Description).NotNull().MaximumLength(4000);
        RuleFor(e => e.Venue).NotEmpty().MaximumLength(200);
        RuleFor(e => e.City).NotEmpty().MaximumLength(100);
        RuleFor(e => e.Category).IsInEnum();
        RuleFor(e => e.EndsAt).GreaterThan(e => e.StartsAt).WithMessage("The event must end after it starts.");
    }
}

internal sealed class EventQueryValidator : AbstractValidator<EventQuery>
{
    public EventQueryValidator()
    {
        this.AddPagingRules();
        RuleFor(q => q.Search).MaximumLength(100);
        RuleFor(q => q.City).MaximumLength(100);
        RuleFor(q => q.Category).IsInEnum().When(q => q.Category.HasValue);
        RuleFor(q => q.SortBy).IsInEnum();
        RuleFor(q => q.To).GreaterThan(q => q.From).When(q => q.From.HasValue && q.To.HasValue);
    }
}
