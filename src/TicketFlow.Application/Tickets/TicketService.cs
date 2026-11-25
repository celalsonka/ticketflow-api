using System.Linq.Expressions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TicketFlow.Application.Abstractions;
using TicketFlow.Application.Common;
using TicketFlow.Domain.Common;
using TicketFlow.Domain.Events;
using TicketFlow.Domain.Tickets;

namespace TicketFlow.Application.Tickets;

public sealed record TicketDto(
    Guid Id,
    string Code,
    Guid EventId,
    string EventTitle,
    string Venue,
    string City,
    DateTimeOffset StartsAt,
    string TierName,
    TicketStatus Status,
    DateTimeOffset IssuedAt,
    DateTimeOffset? CheckedInAt);

public sealed record CheckInRequest(string Code);

public sealed record CheckInResponse(Guid TicketId, string Code, string EventTitle, string TierName, DateTimeOffset CheckedInAt);

internal sealed class CheckInRequestValidator : AbstractValidator<CheckInRequest>
{
    public CheckInRequestValidator() => RuleFor(r => r.Code)
        .NotEmpty()
        .Matches("^(?i)TF-[0-9A-Z]{5}-[0-9A-Z]{5}$")
        .WithMessage("Ticket code must look like TF-XXXXX-XXXXX.");
}

public sealed class TicketService(
    IAppDbContext db,
    ICurrentUser currentUser,
    TimeProvider timeProvider,
    ILogger<TicketService> logger)
{
    private static readonly Expression<Func<Ticket, TicketDto>> ToDto = t => new TicketDto(
        t.Id, t.Code, t.EventId, t.Event.Title, t.Event.Venue, t.Event.City, t.Event.StartsAt,
        t.TicketTier.Name, t.Status, t.IssuedAt, t.CheckedInAt);

    public Task<PagedResult<TicketDto>> GetMineAsync(PageQuery page, CancellationToken ct)
    {
        var userId = currentUser.UserId;
        return db.Tickets.AsNoTracking()
            .Where(t => t.OwnerId == userId)
            .OrderBy(t => t.Event.StartsAt)
            .ThenBy(t => t.Code)
            .Select(ToDto)
            .ToPagedResultAsync(page, ct);
    }

    public async Task<Result<TicketDto>> GetByCodeAsync(string code, CancellationToken ct)
    {
        var normalized = code.Trim().ToUpperInvariant();
        var userId = currentUser.UserId;
        var isAdmin = currentUser.IsAdmin;

        // Visible to its owner, the event's organizer and admins.
        var ticket = await db.Tickets.AsNoTracking()
            .Where(t => t.Code == normalized && (isAdmin || t.OwnerId == userId || t.Event.OrganizerId == userId))
            .Select(ToDto)
            .FirstOrDefaultAsync(ct);

        return ticket is null ? TicketErrors.NotFound : ticket;
    }

    /// <summary>Validates a ticket at the venue entrance. Each ticket admits exactly once.</summary>
    public async Task<Result<CheckInResponse>> CheckInAsync(CheckInRequest request, CancellationToken ct)
    {
        var code = request.Code.Trim().ToUpperInvariant();
        var ticket = await db.Tickets
            .Include(t => t.Event)
            .Include(t => t.TicketTier)
            .FirstOrDefaultAsync(t => t.Code == code, ct);

        if (ticket is null)
        {
            return TicketErrors.NotFound;
        }

        if (!currentUser.IsAdmin && !ticket.Event.IsOrganizedBy(currentUser.UserId))
        {
            return EventErrors.NotOrganizer;
        }

        var now = timeProvider.GetUtcNow();
        var checkedIn = ticket.CheckIn(now);
        if (checkedIn.IsFailure)
        {
            return checkedIn.Error;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Two gates scanned the same ticket at the same moment; only the first one wins.
            return TicketErrors.AlreadyCheckedIn(now);
        }

        logger.LogInformation("Ticket {TicketCode} checked in for event {EventId}", ticket.Code, ticket.EventId);
        return new CheckInResponse(ticket.Id, ticket.Code, ticket.Event.Title, ticket.TicketTier.Name, now);
    }
}
