using Microsoft.Extensions.Logging;
using TicketFlow.Application.Abstractions;

namespace TicketFlow.Infrastructure.Payments;

/// <summary>
/// Stand-in for a real payment provider, mirroring how Stripe/iyzico test tokens behave.
/// Swap this adapter for a real one without touching the application layer.
/// </summary>
internal sealed class FakePaymentGateway(ILogger<FakePaymentGateway> logger) : IPaymentGateway
{
    public const string DeclinedToken = "tok_declined";
    public const string InsufficientFundsToken = "tok_insufficient_funds";

    public Task<PaymentResult> ChargeAsync(PaymentRequest request, CancellationToken cancellationToken)
    {
        var result = request.CardToken switch
        {
            DeclinedToken => PaymentResult.Failure("the card was declined."),
            InsufficientFundsToken => PaymentResult.Failure("insufficient funds."),
            _ => PaymentResult.Success($"pay_{Guid.NewGuid():N}"),
        };

        logger.LogInformation("Charged {Amount} {Currency} for order {OrderId}: {Outcome}",
            request.Amount, request.Currency, request.OrderId, result.Succeeded ? "succeeded" : "failed");

        return Task.FromResult(result);
    }

    public Task RefundAsync(string paymentReference, decimal amount, string currency, CancellationToken cancellationToken)
    {
        logger.LogInformation("Refunded {Amount} {Currency} for payment {PaymentReference}", amount, currency, paymentReference);
        return Task.CompletedTask;
    }
}
