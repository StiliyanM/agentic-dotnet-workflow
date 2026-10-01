namespace ApmPlayground.Api.Payments;

public record CreatePaymentRequest(decimal? Amount, string? Currency, string? Method);
