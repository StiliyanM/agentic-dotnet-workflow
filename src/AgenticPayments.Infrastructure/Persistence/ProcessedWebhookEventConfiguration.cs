using AgenticPayments.Domain.Payments;
using AgenticPayments.Domain.Webhooks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgenticPayments.Infrastructure.Persistence;

public sealed class ProcessedWebhookEventConfiguration : IEntityTypeConfiguration<ProcessedWebhookEvent>
{
    public void Configure(EntityTypeBuilder<ProcessedWebhookEvent> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // The primary key makes a concurrent repeat of the same event fail with a unique violation.
        builder.HasKey(e => e.EventId);
        builder.Property(e => e.EventId).HasMaxLength(ProcessedWebhookEvent.MaxEventIdLength);
        builder.HasOne<Payment>()
            .WithMany()
            .HasForeignKey(e => e.PaymentId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}
