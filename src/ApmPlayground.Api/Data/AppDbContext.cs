using ApmPlayground.Api.Payments;
using Microsoft.EntityFrameworkCore;

namespace ApmPlayground.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Payment> Payments => Set<Payment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<Payment>(payment =>
        {
            payment.Property(p => p.Amount).HasPrecision(Payment.AmountPrecision, Payment.AmountScale);
            payment.Property(p => p.Currency).HasMaxLength(3).IsRequired();
            payment.Property(p => p.Method).HasConversion<string>();
            payment.Property(p => p.Status).HasConversion<string>();
        });
    }
}
