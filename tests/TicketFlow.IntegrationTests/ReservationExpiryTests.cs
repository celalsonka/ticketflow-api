using System.Net;
using Microsoft.Extensions.DependencyInjection;
using TicketFlow.Application.Orders;
using TicketFlow.Domain.Orders;
using TicketFlow.Domain.Users;
using TicketFlow.IntegrationTests.Infrastructure;

namespace TicketFlow.IntegrationTests;

/// <summary>Moves the clock forward, so it gets a private API instance.</summary>
public class ReservationExpiryTests(ExpiryApiFactory factory) : ApiTestBase(factory), IClassFixture<ExpiryApiFactory>
{
    [Fact]
    public async Task Unpaid_reservations_expire_and_their_seats_go_back_on_sale()
    {
        var (organizer, _) = await SignInAsync(UserRole.Organizer);
        var (customer, _) = await SignInAsync();
        var @event = await CreateEventAsync(organizer);
        var tierId = @event.TicketTiers.Single().Id;

        var abandoned = await ReadAsync<OrderDto>(await PlaceOrderAsync(customer, @event.Id, tierId, 3), HttpStatusCode.Created);
        var stillFresh = await ReadAsync<OrderDto>(await PlaceOrderAsync(customer, @event.Id, tierId, 1), HttpStatusCode.Created);
        Assert.Equal(4, await SoldAsync(tierId));

        // 9 minutes later: nothing to expire yet.
        Factory.Clock.Advance(TimeSpan.FromMinutes(9));
        Assert.Equal(0, await RunSweeperAsync());

        // The second order is placed later, so only the first one is overdue at minute 10.
        var later = await ReadAsync<OrderDto>(await PlaceOrderAsync(customer, @event.Id, tierId, 2), HttpStatusCode.Created);
        Factory.Clock.Advance(TimeSpan.FromMinutes(1.5));

        Assert.Equal(2, await RunSweeperAsync());
        Assert.Equal(2, await SoldAsync(tierId)); // only the newest reservation still holds seats

        var expired = await ReadAsync<OrderDto>(await customer.GetAsync($"/api/v1/orders/{abandoned.Id}"));
        Assert.Equal(OrderStatus.Expired, expired.Status);

        var payTooLate = await PayAsync(customer, stillFresh.Id);
        Assert.Equal("Order.NotPending", await ProblemCodeAsync(payTooLate, HttpStatusCode.Conflict));

        var payInTime = await ReadAsync<OrderDto>(await PayAsync(customer, later.Id));
        Assert.Equal(OrderStatus.Paid, payInTime.Status);
    }

    private async Task<int> RunSweeperAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<OrderExpirationService>().ExpireOverdueOrdersAsync(100, CancellationToken.None);
    }
}

public sealed class ExpiryApiFactory : TicketFlowApiFactory;
