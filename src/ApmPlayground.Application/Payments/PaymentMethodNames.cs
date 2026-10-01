using ApmPlayground.Domain.Payments;

namespace ApmPlayground.Application.Payments;

public static class PaymentMethodNames
{
    private const string Ideal = "ideal";
    private const string Klarna = "klarna";

    public static bool TryParse(string? value, out PaymentMethod method)
    {
        switch (value)
        {
            case Ideal:
                method = PaymentMethod.Ideal;
                return true;
            case Klarna:
                method = PaymentMethod.Klarna;
                return true;
            default:
                method = default;
                return false;
        }
    }

    public static string ToName(PaymentMethod method) => method switch
    {
        PaymentMethod.Ideal => Ideal,
        PaymentMethod.Klarna => Klarna,
        _ => throw new ArgumentOutOfRangeException(nameof(method), method, null),
    };
}
