using ApmPlayground.Domain.Payments;

namespace ApmPlayground.Application.Payments;

public record CreatePaymentResponse(Guid PaymentId, Uri RedirectUrl, PaymentStatus Status);
