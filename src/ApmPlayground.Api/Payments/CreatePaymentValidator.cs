namespace ApmPlayground.Api.Payments;

public static class CreatePaymentValidator
{
    public static Dictionary<string, string[]> Validate(CreatePaymentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new Dictionary<string, string[]>();
        AddIfError(errors, "amount", AmountError(request.Amount));
        AddIfError(errors, "currency", CurrencyError(request.Currency));
        AddIfError(errors, "method", MethodError(request.Method));
        return errors;
    }

    private static string? AmountError(decimal? amount) => amount switch
    {
        null => "Amount is required.",
        <= 0 => "Amount must be greater than 0.",
        > Payment.MaxAmount => "Amount is too large.",
        _ when decimal.Round(amount.Value, Payment.AmountScale) != amount.Value => "Amount must have at most 2 decimal places.",
        _ => null,
    };

    private static string? CurrencyError(string? currency) =>
        string.IsNullOrEmpty(currency) ? "Currency is required."
        : SupportedCurrencies.IsSupported(currency) ? null
        : $"Currency '{currency}' is not supported.";

    private static string? MethodError(string? method) =>
        string.IsNullOrEmpty(method) ? "Method is required."
        : PaymentMethodNames.TryParse(method, out _) ? null
        : "Method must be 'ideal' or 'klarna'.";

    private static void AddIfError(Dictionary<string, string[]> errors, string key, string? error)
    {
        if (error is not null)
        {
            errors[key] = [error];
        }
    }
}
