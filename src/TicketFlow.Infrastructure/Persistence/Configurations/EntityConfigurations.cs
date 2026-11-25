using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TicketFlow.Domain.Events;
using TicketFlow.Domain.Orders;
using TicketFlow.Domain.Tickets;
using TicketFlow.Domain.Users;

namespace TicketFlow.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.Property(u => u.Email).HasMaxLength(256);
        builder.Property(u => u.FullName).HasMaxLength(100);
        builder.Property(u => u.PasswordHash).HasMaxLength(512);
        builder.Property(u => u.Role).HasConversion<string>().HasMaxLength(32);
        builder.HasIndex(u => u.Email).IsUnique();
    }
}

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.Property(t => t.TokenHash).HasMaxLength(128);
        builder.Property(t => t.ReplacedByTokenHash).HasMaxLength(128);
        builder.Property(t => t.RevokedReason).HasMaxLength(100);
        builder.Ignore(t => t.IsRevoked);
        builder.HasIndex(t => t.TokenHash).IsUnique();
        builder.HasOne(t => t.User).WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class EventConfiguration : IEntityTypeConfiguration<Event>
{
    public void Configure(EntityTypeBuilder<Event> builder)
    {
        builder.Property(e => e.Title).HasMaxLength(200);
        builder.Property(e => e.Description).HasMaxLength(4000);
        builder.Property(e => e.Venue).HasMaxLength(200);
        builder.Property(e => e.City).HasMaxLength(100);
        builder.Property(e => e.Category).HasConversion<string>().HasMaxLength(32);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(32);

        // Supports the public catalogue query: WHERE Status = 'Published' AND StartsAt >= @now ORDER BY StartsAt
        builder.HasIndex(e => new { e.Status, e.StartsAt });
        builder.HasIndex(e => e.OrganizerId);

        builder.HasOne<User>().WithMany().HasForeignKey(e => e.OrganizerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(e => e.TicketTiers).WithOne().HasForeignKey(t => t.EventId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class TicketTierConfiguration : IEntityTypeConfiguration<TicketTier>
{
    public void Configure(EntityTypeBuilder<TicketTier> builder)
    {
        builder.Property(t => t.Name).HasMaxLength(100);
        builder.Property(t => t.Price).HasPrecision(10, 2);
        builder.Ignore(t => t.Available);
        builder.HasIndex(t => new { t.EventId, t.Name }).IsUnique();

        // Defence in depth: even a buggy code path cannot persist an oversold tier.
        builder.ToTable(t => t.HasCheckConstraint("CK_TicketTiers_Sold", "\"Sold\" >= 0 AND \"Sold\" <= \"Capacity\""));
    }
}

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(o => o.TotalAmount).HasPrecision(12, 2);
        builder.Property(o => o.Currency).HasMaxLength(3).IsFixedLength();
        builder.Property(o => o.PaymentReference).HasMaxLength(100);
        builder.Property(o => o.IdempotencyKey).HasMaxLength(100);

        // One order per (user, Idempotency-Key); NULL keys are not considered equal, so they never collide.
        builder.HasIndex(o => new { o.UserId, o.IdempotencyKey }).IsUnique();
        // Supports the expiration sweeper: WHERE Status = 'Pending' AND ExpiresAt <= @now
        builder.HasIndex(o => new { o.Status, o.ExpiresAt });
        builder.HasIndex(o => o.EventId);

        builder.HasOne(o => o.Event).WithMany().HasForeignKey(o => o.EventId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(o => o.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(o => o.Items).WithOne().HasForeignKey(i => i.OrderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(o => o.Tickets).WithOne().HasForeignKey(t => t.OrderId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.Property(i => i.TierName).HasMaxLength(100);
        builder.Property(i => i.UnitPrice).HasPrecision(10, 2);
        builder.Ignore(i => i.LineTotal);
        builder.HasOne<TicketTier>().WithMany().HasForeignKey(i => i.TicketTierId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class TicketConfiguration : IEntityTypeConfiguration<Ticket>
{
    public void Configure(EntityTypeBuilder<Ticket> builder)
    {
        builder.Property(t => t.Code).HasMaxLength(20);
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(32);
        builder.HasIndex(t => t.Code).IsUnique();
        builder.HasIndex(t => t.OwnerId);
        builder.HasIndex(t => new { t.EventId, t.TicketTierId });

        builder.HasOne(t => t.Event).WithMany().HasForeignKey(t => t.EventId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(t => t.TicketTier).WithMany().HasForeignKey(t => t.TicketTierId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(t => t.OwnerId).OnDelete(DeleteBehavior.Restrict);
    }
}
