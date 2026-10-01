namespace ApmPlayground.Application.Payments;

public record CreatePaymentResponse(Guid PaymentId, string RedirectUrl, string Status);
