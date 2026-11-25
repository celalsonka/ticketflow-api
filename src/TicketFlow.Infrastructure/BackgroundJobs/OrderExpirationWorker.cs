using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TicketFlow.Application.Common;
using TicketFlow.Application.Orders;

namespace TicketFlow.Infrastructure.BackgroundJobs;

/// <summary>Periodically expires unpaid reservations so their seats go back on sale.</summary>
internal sealed class OrderExpirationWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<TicketingOptions> options,
    TimeProvider timeProvider,
    ILogger<OrderExpirationWorker> logger) : BackgroundService
{
    private const int BatchSize = 200;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.EnableExpirationWorker)
        {
            logger.LogInformation("Order expiration worker is disabled");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(settings.ExpirationSweepSeconds), timeProvider);
        try
        {
            do
            {
                await SweepAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Graceful shutdown.
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<OrderExpirationService>();

            // Keep draining while full batches come back (e.g. after downtime).
            while (await service.ExpireOverdueOrdersAsync(BatchSize, ct) == BatchSize)
            {
            }
        }
#pragma warning disable CA1031 // A failing sweep must never kill the host; it is retried on the next tick.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            logger.LogError(ex, "Order expiration sweep failed");
        }
    }
}
