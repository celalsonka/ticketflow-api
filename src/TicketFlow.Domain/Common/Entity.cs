namespace TicketFlow.Domain.Common;

/// <summary>Base type for everything with an identity.</summary>
public abstract class Entity
{
    /// <summary>
    /// Time-ordered UUIDv7 keys keep B-tree indexes compact (inserts append instead of
    /// splitting random pages) while still being safe to generate in the application.
    /// </summary>
    public Guid Id { get; protected init; } = Guid.CreateVersion7();
}

/// <summary>
/// Marks a consistency boundary. Every aggregate root gets an optimistic-concurrency
/// token (see AppDbContext) so two concurrent writers can never silently overwrite each other.
/// </summary>
public abstract class AggregateRoot : Entity;
