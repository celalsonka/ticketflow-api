using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TicketFlow.Application.Auth;
using TicketFlow.Application.Events;
using TicketFlow.Application.Orders;
using TicketFlow.Domain.Events;
using TicketFlow.Domain.Users;
using TicketFlow.Infrastructure.Persistence;

namespace TicketFlow.IntegrationTests.Infrastructure;

public abstract class ApiTestBase(TicketFlowApiFactory factory)
{
    protected const string Password = "Passw0rd!";

    protected static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    protected TicketFlowApiFactory Factory { get; } = factory;

    protected HttpClient Anonymous() => Factory.CreateClient();

    protected static string UniqueEmail(string prefix = "user") => $"{prefix}-{Guid.NewGuid():N}@test.dev";

    /// <summary>Registers a user, assigns the role directly in the database and returns a signed-in client.</summary>
    protected async Task<(HttpClient Client, AuthResponse Auth)> SignInAsync(UserRole role = UserRole.Customer)
    {
        var client = Factory.CreateClient();
        var email = UniqueEmail(role.ToString().ToLowerInvariant());

        var register = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, Password, $"Test {role}"), Json);
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);

        if (role != UserRole.Customer)
        {
            await WithDbAsync(async db =>
            {
                var user = await db.Users.SingleAsync(u => u.Email == email);
                user.ChangeRole(role);
                await db.SaveChangesAsync();
            });
        }

        var auth = await ReadAsync<AuthResponse>(await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, Password), Json));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return (client, auth);
    }

    protected async Task<EventDto> CreateEventAsync(HttpClient organizer, bool publish = true, params TicketTierRequest[] tiers)
    {
        var startsAt = Factory.Clock.GetUtcNow().AddDays(30);
        var request = new CreateEventRequest(
            $"Test Event {Guid.NewGuid():N}",
            "Integration test event",
            "Test Arena",
            "Istanbul",
            EventCategory.Concert,
            startsAt,
            startsAt.AddHours(3),
            tiers.Length > 0 ? tiers : [new TicketTierRequest("Standard", 100m, 100)]);

        var created = await ReadAsync<EventDto>(await organizer.PostAsJsonAsync("/api/v1/events", request, Json), HttpStatusCode.Created);
        if (!publish)
        {
            return created;
        }

        return await ReadAsync<EventDto>(await organizer.PostAsync($"/api/v1/events/{created.Id}/publish", null));
    }

    protected static Task<HttpResponseMessage> PlaceOrderAsync(HttpClient client, Guid eventId, Guid tierId, int quantity, string? idempotencyKey = null)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders")
        {
            Content = JsonContent.Create(new CreateOrderRequest(eventId, [new OrderItemRequest(tierId, quantity)]), options: Json),
        };

        if (idempotencyKey is not null)
        {
            message.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        return client.SendAsync(message);
    }

    protected static Task<HttpResponseMessage> PayAsync(HttpClient client, Guid orderId, string cardToken = "tok_visa") =>
        client.PostAsJsonAsync($"/api/v1/orders/{orderId}/pay", new PayOrderRequest(cardToken), Json);

    protected async Task<int> SoldAsync(Guid tierId)
    {
        var sold = 0;
        await WithDbAsync(async db => sold = await db.TicketTiers.AsNoTracking().Where(t => t.Id == tierId).Select(t => t.Sold).SingleAsync());
        return sold;
    }

    protected async Task WithDbAsync(Func<AppDbContext, Task> action)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    protected static async Task<T> ReadAsync<T>(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"Expected {(int)expected} but got {(int)response.StatusCode}: {body}");
        return JsonSerializer.Deserialize<T>(body, Json)!;
    }

    /// <summary>Asserts an RFC 9457 problem response and returns its machine-readable error code.</summary>
    protected static async Task<string?> ProblemCodeAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"Expected {(int)expected} but got {(int)response.StatusCode}: {body}");
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var document = JsonDocument.Parse(body);
        return document.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
