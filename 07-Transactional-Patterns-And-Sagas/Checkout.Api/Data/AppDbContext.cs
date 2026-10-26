using Checkout.Api.Entities;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Checkout.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<OrderSagaState> SagaStates => Set<OrderSagaState>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Order>(b =>
        {
            b.HasKey(o => o.Id);
            b.Property(o => o.TotalAmount).HasPrecision(18, 2);
        });

        modelBuilder.Entity<OutboxMessage>(b =>
        {
            b.HasKey(m => m.Id);
            b.HasIndex(m => m.ProcessedAt);
        });

        modelBuilder.Entity<OrderSagaState>(b =>
        {
            b.HasKey(s => s.CorrelationId);
        });

        // Нативні таблиці MassTransit EF Core Outbox (Inbox, OutboxMessage, OutboxState)
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}
