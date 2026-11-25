using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TicketFlow.Application.Abstractions;
using TicketFlow.Domain.Events;
using TicketFlow.Domain.Users;

namespace TicketFlow.Infrastructure.Persistence;

/// <summary>Demo accounts and events so the API can be explored right after `dotnet run`.</summary>
internal static class DemoDataSeeder
{
    public const string AdminEmail = "admin@ticketflow.dev";
    public const string OrganizerEmail = "organizer@ticketflow.dev";
    public const string CustomerEmail = "customer@ticketflow.dev";
    public const string DemoPassword = "Passw0rd!";

    public static async Task SeedAsync(AppDbContext db, IPasswordHasher hasher, TimeProvider time, ILogger logger, CancellationToken ct)
    {
        if (await db.Users.AnyAsync(ct))
        {
            return;
        }

        var now = time.GetUtcNow();
        var hash = hasher.Hash(DemoPassword);

        var admin = User.Create(AdminEmail, "Ada Admin", hash, UserRole.Admin, now);
        var organizer = User.Create(OrganizerEmail, "Orhan Organizer", hash, UserRole.Organizer, now);
        var customer = User.Create(CustomerEmail, "Cem Customer", hash, UserRole.Customer, now);
        db.Users.AddRange(admin, organizer, customer);

        var today = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);

        AddEvent(db, organizer.Id, now, publish: true,
            new EventDetails("Istanbul Jazz Night", "An evening of modern jazz by the Bosphorus.", "Bosphorus Stage", "Istanbul",
                EventCategory.Concert, today.AddDays(14).AddHours(17), today.AddDays(14).AddHours(21)),
            ("General Admission", 750m, 400), ("VIP", 2_000m, 50));

        AddEvent(db, organizer.Id, now, publish: true,
            new EventDetails("Ankara Developer Summit", "Two tracks of .NET, cloud and AI talks.", "Congresium", "Ankara",
                EventCategory.Conference, today.AddDays(30).AddHours(6), today.AddDays(30).AddHours(15)),
            ("Student", 250m, 200), ("Standard", 900m, 500), ("Workshop Pass", 1_800m, 40));

        AddEvent(db, organizer.Id, now, publish: true,
            new EventDetails("Aegean Rock Festival", "Three stages, one weekend, twenty bands.", "Seaside Park", "Izmir",
                EventCategory.Festival, today.AddDays(45).AddHours(10), today.AddDays(47).AddHours(20)),
            ("Day Pass", 1_200m, 3_000), ("Weekend Pass", 2_900m, 1_500));

        AddEvent(db, organizer.Id, now, publish: true,
            new EventDetails("Hamlet - Open Air", "Shakespeare's classic under the stars.", "City Amphitheatre", "Istanbul",
                EventCategory.Theatre, today.AddDays(9).AddHours(16), today.AddDays(9).AddHours(19)),
            ("Balcony", 400m, 120), ("Stalls", 650m, 300));

        AddEvent(db, organizer.Id, now, publish: false,
            new EventDetails("Charity Football Match (draft)", "Not published yet - visible only to its organizer.", "Community Stadium", "Bursa",
                EventCategory.Sports, today.AddDays(60).AddHours(15), today.AddDays(60).AddHours(17)),
            ("Tribune", 150m, 5_000));

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded demo data. Log in with {Email} / {Password}", OrganizerEmail, DemoPassword);
    }

    private static void AddEvent(AppDbContext db, Guid organizerId, DateTimeOffset now, bool publish,
        EventDetails details, params (string Name, decimal Price, int Capacity)[] tiers)
    {
        var @event = Event.Create(organizerId, details, now).Value;
        foreach (var (name, price, capacity) in tiers)
        {
            _ = @event.AddTicketTier(name, price, capacity).Value;
        }

        if (publish)
        {
            var published = @event.Publish(now);
            if (published.IsFailure)
            {
                throw new InvalidOperationException(published.Error.Description);
            }
        }

        db.Events.Add(@event);
    }
}
