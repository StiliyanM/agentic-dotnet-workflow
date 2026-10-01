using ApmPlayground.Domain.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ApmPlayground.Infrastructure.Persistence;

public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Property(p => p.Amount).HasPrecision(Payment.AmountPrecision, Payment.AmountScale);
        builder.Property(p => p.Currency).HasMaxLength(3).IsRequired();
        builder.Property(p => p.Method).HasConversion<string>();
        builder.Property(p => p.Status).HasConversion<string>();
    }
}
