using System.Net;
using System.Net.Http.Json;
using TicketFlow.Application.Common;
using TicketFlow.Application.Events;
using TicketFlow.Domain.Events;
using TicketFlow.Domain.Users;
using TicketFlow.IntegrationTests.Infrastructure;

namespace TicketFlow.IntegrationTests;

public class EventTests(TicketFlowApiFactory factory) : ApiTestBase(factory), IClassFixture<TicketFlowApiFactory>
{
    [Fact]
    public async Task Customers_cannot_create_events()
    {
        var (customer, _) = await SignInAsync();
        var startsAt = Factory.Clock.GetUtcNow().AddDays(5);

        var response = await customer.PostAsJsonAsync("/api/v1/events", new CreateEventRequest(
            "Nope", "", "Venue", "City", EventCategory.Other, startsAt, startsAt.AddHours(1), []), Json);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Published_event_appears_in_the_public_catalogue_with_price_and_availability()
    {
        var (organizer, _) = await SignInAsync(UserRole.Organizer);
        var created = await CreateEventAsync(organizer, publish: true,
            new TicketTierRequest("Balcony", 150m, 40), new TicketTierRequest("Floor", 90.5m, 60));

        var page = await ReadAsync<PagedResult<EventSummaryDto>>(
            await Anonymous().GetAsync($"/api/v1/events?search={created.Title[^8..]}&sortBy=Price"));

        var summary = Assert.Single(page.Items);
        Assert.Equal(created.Id, summary.Id);
        Assert.Equal(90.5m, summary.MinPrice);
        Assert.Equal(100, summary.AvailableTickets);
        Assert.Equal(EventStatus.Published, summary.Status);
    }

    [Fact]
    public async Task Draft_events_are_invisible_to_the_public_but_visible_to_their_organizer()
    {
        var (organizer, _) = await SignInAsync(UserRole.Organizer);
        var draft = await CreateEventAsync(organizer, publish: false);

        Assert.Equal(HttpStatusCode.NotFound, (await Anonymous().GetAsync($"/api/v1/events/{draft.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await organizer.GetAsync($"/api/v1/events/{draft.Id}")).StatusCode);

        var managed = await ReadAsync<PagedResult<EventSummaryDto>>(await organizer.GetAsync("/api/v1/events/managed"));
        Assert.Contains(managed.Items, e => e.Id == draft.Id && e.Status == EventStatus.Draft);
    }

    [Fact]
    public async Task Organizers_cannot_modify_each_others_events()
    {
        var (owner, _) = await SignInAsync(UserRole.Organizer);
        var (other, _) = await SignInAsync(UserRole.Organizer);
        var @event = await CreateEventAsync(owner, publish: false);

        var response = await other.PostAsync($"/api/v1/events/{@event.Id}/publish", null);

        Assert.Equal("Event.NotOrganizer", await ProblemCodeAsync(response, HttpStatusCode.Forbidden));
    }

    [Fact]
    public async Task Publishing_without_ticket_tiers_is_rejected()
    {
        var (organizer, _) = await SignInAsync(UserRole.Organizer);
        var startsAt = Factory.Clock.GetUtcNow().AddDays(5);
        var draft = await ReadAsync<EventDto>(await organizer.PostAsJsonAsync("/api/v1/events", new CreateEventRequest(
            "No tiers", "", "Venue", "City", EventCategory.Other, startsAt, startsAt.AddHours(1), []), Json), HttpStatusCode.Created);

        var response = await organizer.PostAsync($"/api/v1/events/{draft.Id}/publish", null);

        Assert.Equal("Event.NoTicketTiers", await ProblemCodeAsync(response, HttpStatusCode.Conflict));
    }

    [Fact]
    public async Task Catalogue_query_is_validated()
    {
        var response = await Anonymous().GetAsync("/api/v1/events?pageSize=500&page=0");

        Assert.Equal("Validation.Failed", await ProblemCodeAsync(response, HttpStatusCode.BadRequest));
    }

    [Fact]
    public async Task Catalogue_is_cached_but_evicted_when_an_event_is_published()
    {
        var (organizer, _) = await SignInAsync(UserRole.Organizer);
        var draft = await CreateEventAsync(organizer, publish: false);
        var url = $"/api/v1/events?search={draft.Title[^8..]}";

        Assert.Empty((await ReadAsync<PagedResult<EventSummaryDto>>(await Anonymous().GetAsync(url))).Items);

        await organizer.PostAsync($"/api/v1/events/{draft.Id}/publish", null);

        Assert.Single((await ReadAsync<PagedResult<EventSummaryDto>>(await Anonymous().GetAsync(url))).Items);
    }
}
