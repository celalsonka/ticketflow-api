using System.Net;
using System.Net.Http.Json;
using TicketFlow.Application.Auth;
using TicketFlow.IntegrationTests.Infrastructure;

namespace TicketFlow.IntegrationTests;

/// <summary>Cross-cutting behaviour: rate limiting, health checks, OpenAPI, headers.</summary>
public class PlatformTests(RateLimitedApiFactory factory) : ApiTestBase(factory), IClassFixture<RateLimitedApiFactory>
{
    [Fact]
    public async Task Login_is_rate_limited_per_client()
    {
        var client = Anonymous();
        var request = new LoginRequest(UniqueEmail(), "Whatever1");

        for (var i = 0; i < RateLimitedApiFactory.AuthLimit; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/auth/login", request, Json)).StatusCode);
        }

        var limited = await client.PostAsJsonAsync("/api/v1/auth/login", request, Json);

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.Contains("Retry-After"));
    }

    [Fact]
    public async Task Health_endpoints_report_healthy()
    {
        var client = Anonymous();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        var ready = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Contains("\"database\"", await ready.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenApi_document_describes_the_api_and_its_security()
    {
        var document = await Anonymous().GetStringAsync("/openapi/v1.json");

        Assert.Contains("TicketFlow API", document, StringComparison.Ordinal);
        Assert.Contains("/api/v1/orders", document, StringComparison.Ordinal);
        Assert.Contains("\"bearer\"", document, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Responses_carry_correlation_and_security_headers_and_problem_details_on_404()
    {
        var response = await Anonymous().GetAsync($"/api/v1/events/{Guid.NewGuid()}");

        Assert.Equal("Event.NotFound", await ProblemCodeAsync(response, HttpStatusCode.NotFound));
        Assert.True(response.Headers.Contains("X-Correlation-Id"));
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
    }
}

public sealed class RateLimitedApiFactory : TicketFlowApiFactory
{
    public const int AuthLimit = 3;

    protected override void ConfigureSettings(IDictionary<string, string?> settings) =>
        settings["RateLimiting:AuthPermitLimit"] = AuthLimit.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
