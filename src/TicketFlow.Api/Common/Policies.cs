namespace TicketFlow.Api.Common;

public static class Policies
{
    public const string RoleClaim = "role";

    /// <summary>Organizers and admins: manage events, view sales, check tickets in.</summary>
    public const string Organizer = "Organizer";

    public const string Admin = "Admin";
}

public static class RateLimitPolicies
{
    /// <summary>Per-IP fixed window on credential endpoints (brute-force protection).</summary>
    public const string Auth = "auth";

    /// <summary>Per-user token bucket on order placement (anti-scalping / bot protection).</summary>
    public const string Orders = "orders";
}

public static class CacheTags
{
    public const string Events = "events";
}

public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public int AuthPermitLimit { get; set; } = 10;
    public int AuthWindowSeconds { get; set; } = 60;
    public int OrdersTokenLimit { get; set; } = 20;
    public int OrdersTokensPerMinute { get; set; } = 20;
}
