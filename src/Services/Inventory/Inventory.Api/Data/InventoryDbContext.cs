using Inventory.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

public class InventoryDbContext : DbContext
{
    public InventoryDbContext(DbContextOptions<InventoryDbContext> options) : base(options) { }

    public DbSet<EventEntity> Events => Set<EventEntity>();
    public DbSet<Seat> Seats => Set<Seat>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EventEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.VenueName).HasMaxLength(200).IsRequired();
            e.Property(x => x.BasePrice).HasColumnType("decimal(10,2)");
        });

        modelBuilder.Entity<Seat>(e =>
        {
            e.HasKey(x => x.Id);

            e.HasOne(x => x.Event)
                .WithMany(x => x.Seats)
                .HasForeignKey(x => x.EventId)
                .OnDelete(DeleteBehavior.Cascade);

            // One row per (event, row, col) — prevents seeding duplicate seats.
            e.HasIndex(x => new { x.EventId, x.Row, x.Col }).IsUnique();

            // Speeds up the background sweep for expired holds.
            e.HasIndex(x => new { x.Status, x.HoldExpiresAt });

            e.Property(x => x.RowVersion).IsRowVersion();
        });
    }
}
