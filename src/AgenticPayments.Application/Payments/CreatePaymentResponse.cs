using AgenticPayments.Domain.Payments;

namespace AgenticPayments.Application.Payments;

public sealed record CreatePaymentResponse(Guid PaymentId, Uri RedirectUrl, PaymentStatus Status);
