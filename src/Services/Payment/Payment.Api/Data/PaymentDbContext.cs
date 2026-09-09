using Payment.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Payment.Api.Data;

public class PaymentDbContext : DbContext
{
    public PaymentDbContext(DbContextOptions<PaymentDbContext> options) : base(options) { }

    public DbSet<PaymentEntity> Payments => Set<PaymentEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PaymentEntity>(e =>
        {
            e.HasKey(x => x.TransactionId);
            e.Property(x => x.Amount).HasColumnType("decimal(10,2)");
            e.Property(x => x.IdempotencyKey).HasMaxLength(100).IsRequired();
            e.HasIndex(x => x.IdempotencyKey).IsUnique();
        });
    }
}
