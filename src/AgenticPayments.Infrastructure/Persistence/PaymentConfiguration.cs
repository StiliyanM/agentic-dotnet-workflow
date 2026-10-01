using AgenticPayments.Domain.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgenticPayments.Infrastructure.Persistence;

public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Property(p => p.Amount).HasPrecision(Payment.AmountPrecision, Payment.AmountScale);
        // The column stores the upper-case ISO code, which is the member name in upper case.
        builder.Property(p => p.Currency)
            .HasConversion(c => c.ToString().ToUpperInvariant(), s => Enum.Parse<Currency>(s, true))
            .HasMaxLength(3)
            .IsRequired();
        builder.Property(p => p.Method).HasConversion<string>();
        builder.Property(p => p.Status).HasConversion<string>();
    }
}
