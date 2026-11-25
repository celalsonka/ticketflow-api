using TicketFlow.Domain.Events;

namespace TicketFlow.UnitTests.Domain;

public class EventTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    internal static EventDetails Details(DateTimeOffset? startsAt = null, DateTimeOffset? endsAt = null)
    {
        var start = startsAt ?? Now.AddDays(10);
        return new EventDetails("  Jazz Night  ", "desc", "Hall", "Istanbul", EventCategory.Concert, start, endsAt ?? start.AddHours(3));
    }

    [Fact]
    public void Create_starts_as_draft_and_trims_and_normalises_to_utc()
    {
        var localStart = new DateTimeOffset(2026, 6, 20, 21, 0, 0, TimeSpan.FromHours(3));

        var result = Event.Create(Guid.NewGuid(), Details(localStart), Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(EventStatus.Draft, result.Value.Status);
        Assert.Equal("Jazz Night", result.Value.Title);
        Assert.Equal(TimeSpan.Zero, result.Value.StartsAt.Offset);
        Assert.Equal(localStart, result.Value.StartsAt);
    }

    [Fact]
    public void Create_rejects_events_in_the_past()
    {
        var result = Event.Create(Guid.NewGuid(), Details(Now.AddMinutes(-1)), Now);

        Assert.Equal(EventErrors.StartsInPast, result.Error);
    }

    [Fact]
    public void Create_rejects_end_before_start()
    {
        var start = Now.AddDays(1);
        var result = Event.Create(Guid.NewGuid(), Details(start, start.AddHours(-1)), Now);

        Assert.Equal(EventErrors.InvalidSchedule, result.Error);
    }

    [Fact]
    public void Publish_requires_at_least_one_ticket_tier()
    {
        var @event = Event.Create(Guid.NewGuid(), Details(), Now).Value;

        Assert.Equal(EventErrors.NoTicketTiers, @event.Publish(Now).Error);

        @event.AddTicketTier("Standard", 100, 10);
        Assert.True(@event.Publish(Now).IsSuccess);
        Assert.Equal(EventStatus.Published, @event.Status);
        Assert.True(@event.IsOnSale(Now));
    }

    [Fact]
    public void Publish_twice_is_rejected()
    {
        var @event = Event.Create(Guid.NewGuid(), Details(), Now).Value;
        @event.AddTicketTier("Standard", 100, 10);
        @event.Publish(Now);

        Assert.Equal(EventErrors.NotDraft, @event.Publish(Now).Error);
    }

    [Fact]
    public void Tier_names_are_unique_case_insensitively()
    {
        var @event = Event.Create(Guid.NewGuid(), Details(), Now).Value;
        @event.AddTicketTier("VIP", 500, 10);

        var duplicate = @event.AddTicketTier("vip", 400, 5);

        Assert.True(duplicate.IsFailure);
        Assert.Equal("Event.DuplicateTier", duplicate.Error.Code);
    }

    [Theory]
    [InlineData(-1, 10)]
    [InlineData(10, 0)]
    public void Tier_requires_valid_price_and_capacity(decimal price, int capacity)
    {
        var @event = Event.Create(Guid.NewGuid(), Details(), Now).Value;

        Assert.Equal(EventErrors.InvalidTier, @event.AddTicketTier("X", price, capacity).Error);
    }

    [Fact]
    public void Cancelled_event_cannot_be_modified_or_cancelled_again()
    {
        var @event = Event.Create(Guid.NewGuid(), Details(), Now).Value;
        Assert.True(@event.Cancel(Now).IsSuccess);

        Assert.Equal(EventErrors.AlreadyCancelled, @event.Cancel(Now).Error);
        Assert.Equal(EventErrors.AlreadyCancelled, @event.Update(Details(), Now).Error);
        Assert.Equal(EventErrors.AlreadyCancelled, @event.AddTicketTier("New", 1, 1).Error);
        Assert.False(@event.IsOnSale(Now));
    }

    [Fact]
    public void Event_is_not_on_sale_once_it_has_started()
    {
        var @event = Event.Create(Guid.NewGuid(), Details(), Now).Value;
        @event.AddTicketTier("Standard", 100, 10);
        @event.Publish(Now);

        Assert.False(@event.IsOnSale(@event.StartsAt));
    }
}
