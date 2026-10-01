namespace ApmPlayground.Application.Payments;

public record CreatePaymentRequest(decimal? Amount, string? Currency, string? Method);
