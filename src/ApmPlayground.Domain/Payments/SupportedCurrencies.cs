namespace ApmPlayground.Domain.Payments;

public static class SupportedCurrencies
{
    public static IReadOnlySet<string> All { get; } = new HashSet<string>(["EUR", "GBP", "USD"], StringComparer.Ordinal);

    public static bool IsSupported(string? code) => code is not null && All.Contains(code);
}
