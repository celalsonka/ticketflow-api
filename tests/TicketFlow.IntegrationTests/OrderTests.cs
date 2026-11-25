using System.Net;
using System.Net.Http.Json;
using TicketFlow.Application.Events;
using TicketFlow.Application.Orders;
using TicketFlow.Application.Tickets;
using TicketFlow.Domain.Orders;
using TicketFlow.Domain.Tickets;
using TicketFlow.Domain.Users;
using TicketFlow.IntegrationTests.Infrastructure;

namespace TicketFlow.IntegrationTests;

public class OrderTests(TicketFlowApiFactory factory) : ApiTestBase(factory), IClassFixture<TicketFlowApiFactory>
{
    [Fact]
    public async Task Full_purchase_flow_reserve_pay_check_in_and_report()
    {
        var (organizer, _) = await SignInAsync(UserRole.Organizer);
        var (customer, _) = await SignInAsync();
        var @event = await CreateEventAsync(organizer, publish: true,
            new TicketTierRequest("Standard", 100m, 50), new TicketTierRequest("VIP", 300m, 10));
        var vip = @event.TicketTiers.Single(t => t.Name == "VIP");

        // 1. Reserve
        var order = await ReadAsync<OrderDto>(await PlaceOrderAsync(customer, @event.Id, vip.Id, 2), HttpStatusCode.Created);
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal(600m, order.TotalAmount);
        Assert.Equal(2, await SoldAsync(vip.Id));

        // 2. Pay -> tickets issued
        var paid = await ReadAsync<OrderDto>(await PayAsync(customer, order.Id));
        Assert.Equal(OrderStatus.Paid, paid.Status);
        Assert.Equal(2, paid.Tickets.Count);

        var myTickets = await ReadAsync<Application.Common.PagedResult<TicketDto>>(await customer.GetAsync("/api/v1/tickets"));
        Assert.Equal(2, myTickets.TotalCount);

        // 3. Check in at the door - only once
        var code = paid.Tickets[0].Code;
        var checkIn = await ReadAsync<CheckInResponse>(await organizer.PostAsJsonAsync("/api/v1/tickets/check-in", new CheckInRequest(code), Json));
        Assert.Equal("VIP", checkIn.TierName);

        var again = await organizer.PostAsJsonAsync("/api/v1/tickets/check-in", new CheckInRequest(code), Json);
        Assert.Equal("Ticket.AlreadyCheckedIn", await ProblemCodeAsync(again, HttpStatusCode.Conflict));

        // Customers cannot check tickets in.
        var byCustomer = await customer.PostAsJsonAsync("/api/v1/tickets/check-in", new CheckInRequest(paid.Tickets[1].Code), Json);
        Assert.Equal(HttpStatusCode.Forbidden, byCustomer.StatusCode);

        // 4. Organizer report
        var report = await ReadAsync<EventSalesReport>(await organizer.GetAsync($"/api/v1/events/{@event.Id}/sales-report"));
        Assert.Equal(2, report.TicketsSold);
        Assert.Equal(1, report.TicketsCheckedIn);
        Assert.Equal(600m, report.Revenue);
        Assert.Equal(0, report.TicketsReserved);
    }

    [Fact]
    public async Task Concurrent_buyers_can_never_oversell_a_tier()
    {
        const int capacity = 5;
        const int buyers = 25;

        var (organizer, _) = await SignInAsync(UserRole.Organizer);
        var @event = await CreateEventAsync(organizer, publish: true, new TicketTierRequest("Last seats", 50m, capacity));
        var tierId = @event.TicketTiers.Single().Id;

        var clients = new List<HttpClient>();
        for (var i = 0; i < buyers; i++)
        {
            clients.Add((await SignInAsync()).Client);
        }

        // Everyone clicks "buy" at the same moment.
        using var start = new SemaphoreSlim(0);
        var attempts = clients.Select(async client =>
        {
            await start.WaitAsync();
            return await PlaceOrderAsync(client, @event.Id, tierId, 1);
        }).ToList();
        start.Release(buyers);
        var responses = await Task.WhenAll(attempts);

        Assert.Equal(capacity, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(buyers - capacity, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(capacity, await SoldAsync(tierId));
    }

    [Fact]
    public async Task Retrying_with_the_same_idempotency_key_returns_the_original_order()
    {
        var (organizer, _) = await SignInAsync(UserRole.Organizer);
        var (customer, _) = await SignInAsync();
        var @event = await CreateEventAsync(organizer);
        var tierId = @event.TicketTiers.Single().Id;
        var key = Guid.NewGuid().ToString();

        var first = await PlaceOrderAsync(customer, @event.Id, tierId, 3, key);
        var retry = await PlaceOrderAsync(customer, @event.Id, tierId, 3, key);

        var original = await ReadAsync<OrderDto>(first, HttpStatusCode.Created);
        var replayed = await ReadAsync<OrderDto>(retry, HttpStatusCode.OK);
        Assert.Equal(original.Id, replayed.Id);
        Assert.Equal("true", retry.Headers.GetValues("Idempotent-Replayed").Single());
        Assert.Equal(3, await SoldAsync(tierId)); // seats reserved once
    }

    [Fact]
    public async Task Declined_payment_keeps_the_reservation_so_the_customer_can_retry()
    {
        var (organizer, _) = await SignInAsync(UserRole.Organizer);
        var (customer, _) = await SignInAsync();
        var @event = await CreateEventAsync(organizer);
        var order = await ReadAsync<OrderDto>(await PlaceOrderAsync(customer, @event.Id, @event.TicketTiers.Single().Id, 1), HttpStatusCode.Created);

        var declined = await PayAsync(customer, order.Id, "tok_declined");
        Assert.Equal("Order.PaymentFailed", await ProblemCodeAsync(declined, HttpStatusCode.PaymentRequired));

        var paid = await ReadAsync<OrderDto>(await PayAsync(customer, order.Id));
        Assert.Equal(OrderStatus.Paid, paid.Status);

        var twice = await PayAsync(customer, order.Id);
        Assert.Equal("Order.NotPending", await ProblemCodeAsync(twice, HttpStatusCode.Conflict));
    }

    [Fact]
    public async Task Cancelling_a_pending_order_releases_its_seats()
    {
        var (organizer, _) = await SignInAsync(UserRole.Organizer);
        var (customer, _) = await SignInAsync();
        var @event = await CreateEventAsync(organizer);
        var tierId = @event.TicketTiers.Single().Id;
        var order = await ReadAsync<OrderDto>(await PlaceOrderAsync(customer, @event.Id, tierId, 4), HttpStatusCode.Created);
        Assert.Equal(4, await SoldAsync(tierId));

        var cancelled = await ReadAsync<OrderDto>(await customer.PostAsync($"/api/v1/orders/{order.Id}/cancel", null));

        Assert.Equal(OrderStatus.Cancelled, cancelled.Status);
        Assert.Equal(0, await SoldAsync(tierId));
    }

    [Fact]
    public async Task Orders_of_other_users_are_not_visible()
    {
        var (organizer, _) = await SignInAsync(UserRole.Organizer);
        var (owner, _) = await SignInAsync();
        var (stranger, _) = await SignInAsync();
        var @event = await CreateEventAsync(organizer);
        var order = await ReadAsync<OrderDto>(await PlaceOrderAsync(owner, @event.Id, @event.TicketTiers.Single().Id, 1), HttpStatusCode.Created);

        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/v1/orders/{order.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PayAsync(stranger, order.Id)).StatusCode);
    }

    [Fact]
    public async Task Order_limits_and_sale_status_are_enforced()
    {
        var (organizer, _) = await SignInAsync(UserRole.Organizer);
        var (customer, _) = await SignInAsync();
        var published = await CreateEventAsync(organizer);
        var draft = await CreateEventAsync(organizer, publish: false);

        var tooMany = await PlaceOrderAsync(customer, published.Id, published.TicketTiers.Single().Id, 11);
        Assert.Equal("Order.TooManyTickets", await ProblemCodeAsync(tooMany, HttpStatusCode.BadRequest));

        var notOnSale = await PlaceOrderAsync(customer, draft.Id, draft.TicketTiers.Single().Id, 1);
        Assert.Equal("Event.NotOnSale", await ProblemCodeAsync(notOnSale, HttpStatusCode.Conflict));

        var wrongTier = await PlaceOrderAsync(customer, published.Id, draft.TicketTiers.Single().Id, 1);
        Assert.Equal("Order.TierNotInEvent", await ProblemCodeAsync(wrongTier, HttpStatusCode.BadRequest));
    }

    [Fact]
    public async Task Cancelling_an_event_refunds_paid_orders_and_voids_tickets()
    {
        var (organizer, _) = await SignInAsync(UserRole.Organizer);
        var (customer, _) = await SignInAsync();
        var @event = await CreateEventAsync(organizer);
        var tierId = @event.TicketTiers.Single().Id;

        var paidOrder = await ReadAsync<OrderDto>(await PlaceOrderAsync(customer, @event.Id, tierId, 2), HttpStatusCode.Created);
        await PayAsync(customer, paidOrder.Id);
        var pendingOrder = await ReadAsync<OrderDto>(await PlaceOrderAsync(customer, @event.Id, tierId, 1), HttpStatusCode.Created);

        await ReadAsync<EventDto>(await organizer.PostAsync($"/api/v1/events/{@event.Id}/cancel", null));

        var refunded = await ReadAsync<OrderDto>(await customer.GetAsync($"/api/v1/orders/{paidOrder.Id}"));
        var voided = await ReadAsync<OrderDto>(await customer.GetAsync($"/api/v1/orders/{pendingOrder.Id}"));
        Assert.Equal(OrderStatus.Refunded, refunded.Status);
        Assert.All(refunded.Tickets, t => Assert.Equal(TicketStatus.Cancelled, t.Status));
        Assert.Equal(OrderStatus.Cancelled, voided.Status);

        var buyAfterCancel = await PlaceOrderAsync(customer, @event.Id, tierId, 1);
        Assert.Equal(HttpStatusCode.Conflict, buyAfterCancel.StatusCode);
    }
}
