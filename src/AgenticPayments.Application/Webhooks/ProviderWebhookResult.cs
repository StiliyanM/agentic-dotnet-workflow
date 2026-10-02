namespace AgenticPayments.Application.Webhooks;

public sealed class ProviderWebhookResult
{
    private ProviderWebhookResult(ProviderWebhookOutcome outcome, IDictionary<string, string[]> errors)
    {
        Outcome = outcome;
        Errors = errors;
    }

    public ProviderWebhookOutcome Outcome { get; }

    public IDictionary<string, string[]> Errors { get; }

    public static ProviderWebhookResult Processed() => new(ProviderWebhookOutcome.Processed, new Dictionary<string, string[]>());

    public static ProviderWebhookResult Ignored() => new(ProviderWebhookOutcome.Ignored, new Dictionary<string, string[]>());

    public static ProviderWebhookResult Duplicate() => new(ProviderWebhookOutcome.Duplicate, new Dictionary<string, string[]>());

    public static ProviderWebhookResult DuplicatePayloadMismatch() =>
        new(ProviderWebhookOutcome.DuplicatePayloadMismatch, new Dictionary<string, string[]>());

    public static ProviderWebhookResult PaymentNotFound() =>
        new(ProviderWebhookOutcome.PaymentNotFound, new Dictionary<string, string[]>());

    public static ProviderWebhookResult Invalid(IDictionary<string, string[]> errors) => new(ProviderWebhookOutcome.Invalid, errors);
}
