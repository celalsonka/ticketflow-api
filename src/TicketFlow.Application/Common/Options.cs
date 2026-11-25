namespace TicketFlow.Application.Common;

public sealed class TicketingOptions
{
    public const string SectionName = "Ticketing";

    /// <summary>How long seats are held for an unpaid order.</summary>
    public int ReservationHoldMinutes { get; set; } = 10;

    public int MaxTicketsPerOrder { get; set; } = 10;

    public string Currency { get; set; } = "TRY";

    public bool EnableExpirationWorker { get; set; } = true;

    public int ExpirationSweepSeconds { get; set; } = 30;
}

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "TicketFlow";
    public string Audience { get; set; } = "TicketFlow.Clients";

    /// <summary>HMAC-SHA256 signing key. Must be at least 32 characters; supply via secrets/env in real deployments.</summary>
    public string Secret { get; set; } = string.Empty;

    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 7;
}
