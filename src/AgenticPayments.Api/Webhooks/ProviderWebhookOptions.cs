namespace AgenticPayments.Api.Webhooks;

public sealed class ProviderWebhookOptions
{
    public const string SectionName = "Webhooks:Provider";

    public string Secret { get; set; } = string.Empty;
}
