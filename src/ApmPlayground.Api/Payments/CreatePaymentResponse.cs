namespace ApmPlayground.Api.Payments;

public record CreatePaymentResponse(Guid PaymentId, string RedirectUrl, string Status);
