using Booking.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Booking.Api.Data;

public class BookingDbContext : DbContext
{
    public BookingDbContext(DbContextOptions<BookingDbContext> options) : base(options) { }

    public DbSet<BookingEntity> Bookings => Set<BookingEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BookingEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Amount).HasColumnType("decimal(10,2)");
            e.Property(x => x.IdempotencyKey).HasMaxLength(100).IsRequired();

            // The core idempotency guarantee: the DB itself rejects a second
            // booking row for the same key, independent of any in-app check.
            e.HasIndex(x => x.IdempotencyKey).IsUnique();
        });
    }
}
