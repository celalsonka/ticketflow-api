using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Context;

namespace TicketFlow.Api.Extensions;

internal static class WebApplicationExtensions
{
    private const string CorrelationHeader = "X-Correlation-Id";

    public static WebApplication UseApi(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages();

        app.Use(CorrelationAndSecurityHeadersAsync);
        app.UseSerilogRequestLogging();

        if (!app.Environment.IsDevelopment())
        {
            app.UseHsts();
        }

        app.UseCors();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();
        app.UseOutputCache();

        app.MapOpenApi();
        app.MapScalarApiReference(options => options
            .WithTitle("TicketFlow API")
            .WithTheme(ScalarTheme.Purple));
        app.MapGet("/", () => Results.Redirect("/scalar/v1")).ExcludeFromDescription();

        // Liveness: the process is up. Readiness: dependencies (database) are reachable.
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false, ResponseWriter = WriteHealthAsync });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready"), ResponseWriter = WriteHealthAsync });

        app.MapControllers();
        return app;
    }

    /// <summary>
    /// Echoes/creates a correlation id (pushed into every log line of the request) and sets
    /// conservative security headers suitable for a JSON API.
    /// </summary>
    private static async Task CorrelationAndSecurityHeadersAsync(HttpContext context, Func<Task> next)
    {
        var correlationId = context.Request.Headers[CorrelationHeader].FirstOrDefault() is { Length: > 0 and <= 64 } supplied
            ? supplied
            : context.TraceIdentifier;

        var headers = context.Response.Headers;
        headers[CorrelationHeader] = correlationId;
        headers.XContentTypeOptions = "nosniff";
        headers.XFrameOptions = "DENY";
        headers["Referrer-Policy"] = "no-referrer";

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next();
        }
    }

    private static Task WriteHealthAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        return context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            totalDurationMs = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                durationMs = e.Value.Duration.TotalMilliseconds,
                error = e.Value.Exception?.Message,
            }),
        }));
    }
}
