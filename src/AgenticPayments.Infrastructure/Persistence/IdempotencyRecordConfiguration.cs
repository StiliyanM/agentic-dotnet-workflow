using AgenticPayments.Domain.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgenticPayments.Infrastructure.Persistence;

public sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // The primary key makes a concurrent first use of the same key fail with a unique violation.
        builder.HasKey(r => r.Key);
        builder.Property(r => r.Key).HasMaxLength(IdempotencyRecord.MaxKeyLength);
        builder.Property(r => r.RequestHash).HasMaxLength(IdempotencyRecord.RequestHashLength).IsRequired();
        builder.HasOne<Payment>()
            .WithMany()
            .HasForeignKey(r => r.PaymentId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
        // The xmin token (no real column) makes a concurrent renewal of the same expired key fail.
        builder.Property<uint>("Version").IsRowVersion();
    }
}
