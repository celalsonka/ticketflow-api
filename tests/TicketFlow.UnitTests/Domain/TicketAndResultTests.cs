using System.Text.RegularExpressions;
using TicketFlow.Domain.Common;
using TicketFlow.Domain.Events;
using TicketFlow.Domain.Orders;
using TicketFlow.Domain.Tickets;

namespace TicketFlow.UnitTests.Domain;

public partial class TicketAndResultTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    [GeneratedRegex("^TF-[0-9A-HJKMNP-TV-Z]{5}-[0-9A-HJKMNP-TV-Z]{5}$")]
    private static partial Regex CodeFormat();

    private static Ticket IssueTicket()
    {
        var @event = Event.Create(Guid.NewGuid(), EventTests.Details(), Now).Value;
        var tier = @event.AddTicketTier("Standard", 10, 10).Value;
        var order = Order.Create(Guid.NewGuid(), @event.Id, [(tier, 1)], "TRY", Now, TimeSpan.FromMinutes(10), null).Value;
        order.MarkPaid("pay", Now);
        return order.Tickets.Single();
    }

    [Fact]
    public void Ticket_codes_use_the_readable_alphabet_and_do_not_repeat()
    {
        var codes = Enumerable.Range(0, 5_000).Select(_ => Ticket.GenerateCode()).ToList();

        Assert.All(codes, c => Assert.Matches(CodeFormat(), c));
        Assert.Equal(codes.Count, codes.Distinct().Count());
    }

    [Fact]
    public void Ticket_admits_only_once()
    {
        var ticket = IssueTicket();

        Assert.True(ticket.CheckIn(Now).IsSuccess);
        Assert.Equal(TicketStatus.CheckedIn, ticket.Status);

        var second = ticket.CheckIn(Now.AddMinutes(1));
        Assert.Equal("Ticket.AlreadyCheckedIn", second.Error.Code);
    }

    [Fact]
    public void Result_guards_its_invariants()
    {
        Result<int> success = 42;
        Result<int> failure = OrderErrors.Empty;

        Assert.Equal(42, success.Value);
        Assert.Equal(Error.None, success.Error);
        Assert.True(failure.IsFailure);
        Assert.Throws<InvalidOperationException>(() => failure.Value);
        Assert.Throws<ArgumentException>(() => Result.Failure(Error.None));
    }
}
