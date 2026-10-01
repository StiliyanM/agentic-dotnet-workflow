using ApmPlayground.Domain.Payments;

namespace ApmPlayground.Application.Payments;

public sealed record CreatePaymentResponse(Guid PaymentId, Uri RedirectUrl, PaymentStatus Status);
