using Microsoft.EntityFrameworkCore;
using Outbox.Resilience.Demo.Entities;

namespace Outbox.Resilience.Demo.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<OutboxMessage>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.EventType).IsRequired().HasMaxLength(200);
            b.Property(x => x.Payload).IsRequired();
            b.HasIndex(x => x.ProcessedAt);
        });
    }
}
