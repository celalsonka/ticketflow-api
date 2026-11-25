using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TicketFlow.Application.Abstractions;
using TicketFlow.Application.Common;
using TicketFlow.Domain.Common;
using TicketFlow.Domain.Events;
using TicketFlow.Domain.Orders;
using TicketFlow.Domain.Tickets;

namespace TicketFlow.Application.Events;

public sealed class EventService(
    IAppDbContext db,
    ICurrentUser currentUser,
    IPaymentGateway paymentGateway,
    TimeProvider timeProvider,
    IOptions<TicketingOptions> options,
    ILogger<EventService> logger)
{
    public async Task<Result<EventDto>> CreateAsync(CreateEventRequest request, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var details = new EventDetails(request.Title, request.Description, request.Venue, request.City, request.Category, request.StartsAt, request.EndsAt);

        var created = Event.Create(currentUser.UserId, details, now);
        if (created.IsFailure)
        {
            return created.Error;
        }

        var @event = created.Value;
        foreach (var tier in request.TicketTiers)
        {
            var added = @event.AddTicketTier(tier.Name, tier.Price, tier.Capacity);
            if (added.IsFailure)
            {
                return added.Error;
            }
        }

        db.Events.Add(@event);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Event {EventId} created by organizer {OrganizerId}", @event.Id, @event.OrganizerId);
        return EventDto.From(@event);
    }

    public async Task<Result<EventDto>> UpdateAsync(Guid eventId, UpdateEventRequest request, CancellationToken ct)
    {
        var loaded = await LoadManagedEventAsync(eventId, ct);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        var @event = loaded.Value;
        var details = new EventDetails(request.Title, request.Description, request.Venue, request.City, request.Category, request.StartsAt, request.EndsAt);
        var updated = @event.Update(details, timeProvider.GetUtcNow());
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        await db.SaveChangesAsync(ct);
        return EventDto.From(@event);
    }

    public async Task<Result<TicketTierDto>> AddTicketTierAsync(Guid eventId, TicketTierRequest request, CancellationToken ct)
    {
        var loaded = await LoadManagedEventAsync(eventId, ct);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        var added = loaded.Value.AddTicketTier(request.Name, request.Price, request.Capacity);
        if (added.IsFailure)
        {
            return added.Error;
        }

        var tier = added.Value;
        db.TicketTiers.Add(tier);
        await db.SaveChangesAsync(ct);

        return new TicketTierDto(tier.Id, tier.Name, tier.Price, tier.Capacity, tier.Sold, tier.Available);
    }

    public async Task<Result<EventDto>> PublishAsync(Guid eventId, CancellationToken ct)
    {
        var loaded = await LoadManagedEventAsync(eventId, ct);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        var published = loaded.Value.Publish(timeProvider.GetUtcNow());
        if (published.IsFailure)
        {
            return published.Error;
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Event {EventId} published", eventId);
        return EventDto.From(loaded.Value);
    }

    /// <summary>
    /// Cancels the event, voids pending reservations and refunds every paid order.
    /// The event and its orders are saved in one SaveChanges call (one transaction), so they never disagree.
    /// </summary>
    public async Task<Result<EventDto>> CancelAsync(Guid eventId, CancellationToken ct)
    {
        var loaded = await LoadManagedEventAsync(eventId, ct);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        var @event = loaded.Value;
        var now = timeProvider.GetUtcNow();
        var cancelled = @event.Cancel(now);
        if (cancelled.IsFailure)
        {
            return cancelled.Error;
        }

        var openOrders = await db.Orders
            .Include(o => o.Tickets)
            .Where(o => o.EventId == eventId && (o.Status == OrderStatus.Pending || o.Status == OrderStatus.Paid))
            .ToListAsync(ct);

        foreach (var order in openOrders)
        {
            if (order is { Status: OrderStatus.Paid, PaymentReference: not null })
            {
                await paymentGateway.RefundAsync(order.PaymentReference, order.TotalAmount, order.Currency, ct);
            }

            order.CloseBecauseEventCancelled(now);
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Event {EventId} cancelled; {OrderCount} orders closed", eventId, openOrders.Count);
        return EventDto.From(@event);
    }

    public async Task<Result<EventDto>> GetByIdAsync(Guid eventId, CancellationToken ct)
    {
        var @event = await db.Events.AsNoTracking().Include(e => e.TicketTiers).FirstOrDefaultAsync(e => e.Id == eventId, ct);
        if (@event is null)
        {
            return EventErrors.NotFound;
        }

        // Drafts are private to their organizer (and admins). Respond 404 rather than 403 so their existence is not leaked.
        if (@event.Status == EventStatus.Draft && !CanManage(@event))
        {
            return EventErrors.NotFound;
        }

        return EventDto.From(@event);
    }

    /// <summary>Public catalogue: published, upcoming events with filtering, sorting and paging.</summary>
    public Task<PagedResult<EventSummaryDto>> SearchAsync(EventQuery query, CancellationToken ct)
    {
        var from = (query.From ?? timeProvider.GetUtcNow()).ToUniversalTime();

        var events = db.Events.AsNoTracking()
            .Where(e => e.Status == EventStatus.Published && e.StartsAt >= from);

        if (query.To is { } to)
        {
            var toUtc = to.ToUniversalTime();
            events = events.Where(e => e.StartsAt < toUtc);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLower();
            events = events.Where(e => e.Title.ToLower().Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(query.City))
        {
            var city = query.City.Trim().ToLower();
            events = events.Where(e => e.City.ToLower() == city);
        }

        if (query.Category is { } category)
        {
            events = events.Where(e => e.Category == category);
        }

        events = (query.SortBy, query.Descending) switch
        {
            (EventSortBy.Title, false) => events.OrderBy(e => e.Title),
            (EventSortBy.Title, true) => events.OrderByDescending(e => e.Title),
            (EventSortBy.Price, false) => events.OrderBy(e => e.TicketTiers.Min(t => t.Price)).ThenBy(e => e.StartsAt),
            (EventSortBy.Price, true) => events.OrderByDescending(e => e.TicketTiers.Min(t => t.Price)).ThenBy(e => e.StartsAt),
            (_, true) => events.OrderByDescending(e => e.StartsAt),
            _ => events.OrderBy(e => e.StartsAt),
        };

        return events.Select(ToSummary).ToPagedResultAsync(query, ct);
    }

    /// <summary>Events managed by the caller (admins see every event), in every status.</summary>
    public Task<PagedResult<EventSummaryDto>> GetManagedAsync(PageQuery page, CancellationToken ct)
    {
        var events = db.Events.AsNoTracking();
        if (!currentUser.IsAdmin)
        {
            var organizerId = currentUser.UserId;
            events = events.Where(e => e.OrganizerId == organizerId);
        }

        return events
            .OrderByDescending(e => e.CreatedAt)
            .Select(ToSummary)
            .ToPagedResultAsync(page, ct);
    }

    public async Task<Result<EventSalesReport>> GetSalesReportAsync(Guid eventId, CancellationToken ct)
    {
        var @event = await db.Events.AsNoTracking().Include(e => e.TicketTiers).FirstOrDefaultAsync(e => e.Id == eventId, ct);
        if (@event is null)
        {
            return EventErrors.NotFound;
        }

        if (!CanManage(@event))
        {
            return EventErrors.NotOrganizer;
        }

        // Counts are aggregated in the database; money is summed in memory to stay provider-agnostic.
        var ticketStats = await db.Tickets.AsNoTracking()
            .Where(t => t.EventId == eventId && t.Status != TicketStatus.Cancelled)
            .GroupBy(t => t.TicketTierId)
            .Select(g => new TierTicketStats(g.Key, g.Count(), g.Count(t => t.Status == TicketStatus.CheckedIn)))
            .ToDictionaryAsync(x => x.TierId, ct);

        var paidLines = await db.Orders.AsNoTracking()
            .Where(o => o.EventId == eventId && o.Status == OrderStatus.Paid)
            .SelectMany(o => o.Items)
            .Select(i => new { i.TicketTierId, i.UnitPrice, i.Quantity })
            .ToListAsync(ct);

        var reserved = await db.Orders.AsNoTracking()
            .Where(o => o.EventId == eventId && o.Status == OrderStatus.Pending)
            .SelectMany(o => o.Items)
            .GroupBy(i => i.TicketTierId)
            .Select(g => new { TierId = g.Key, Quantity = g.Sum(i => i.Quantity) })
            .ToDictionaryAsync(x => x.TierId, x => x.Quantity, ct);

        var tiers = @event.TicketTiers
            .OrderBy(t => t.Price)
            .Select(t =>
            {
                var stats = ticketStats.GetValueOrDefault(t.Id);
                var revenue = paidLines.Where(l => l.TicketTierId == t.Id).Sum(l => l.UnitPrice * l.Quantity);
                return new TierSalesDto(t.Id, t.Name, t.Price, t.Capacity, stats?.Issued ?? 0,
                    reserved.GetValueOrDefault(t.Id), stats?.CheckedIn ?? 0, revenue);
            })
            .ToList();

        var capacity = tiers.Sum(t => t.Capacity);
        var sold = tiers.Sum(t => t.TicketsSold);

        return new EventSalesReport(
            @event.Id,
            @event.Title,
            @event.Status,
            capacity,
            sold,
            tiers.Sum(t => t.TicketsReserved),
            tiers.Sum(t => t.TicketsCheckedIn),
            tiers.Sum(t => t.Revenue),
            options.Value.Currency,
            capacity == 0 ? 0 : Math.Round((double)sold / capacity, 4),
            tiers);
    }

    private async Task<Result<Event>> LoadManagedEventAsync(Guid eventId, CancellationToken ct)
    {
        var @event = await db.Events.Include(e => e.TicketTiers).FirstOrDefaultAsync(e => e.Id == eventId, ct);
        if (@event is null)
        {
            return EventErrors.NotFound;
        }

        return CanManage(@event) ? @event : EventErrors.NotOrganizer;
    }

    private bool CanManage(Event @event) =>
        currentUser.IsAuthenticated && (currentUser.IsAdmin || @event.IsOrganizedBy(currentUser.UserId));

    private static readonly System.Linq.Expressions.Expression<Func<Event, EventSummaryDto>> ToSummary = e => new EventSummaryDto(
        e.Id,
        e.Title,
        e.Venue,
        e.City,
        e.Category,
        e.StartsAt,
        e.Status,
        e.TicketTiers.Min(t => (decimal?)t.Price),
        e.TicketTiers.Sum(t => t.Capacity - t.Sold));

    private sealed record TierTicketStats(Guid TierId, int Issued, int CheckedIn);
}
