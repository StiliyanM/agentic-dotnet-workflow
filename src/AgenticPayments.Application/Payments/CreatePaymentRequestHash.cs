using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AgenticPayments.Domain.Payments;

namespace AgenticPayments.Application.Payments;

// Hashes the meaning of the request, so JSON formatting, case and amount scale do not change it.
// Call it only after validation: F2 is exact because a valid amount has at most 2 decimals.
public static class CreatePaymentRequestHash
{
    public static string Compute(decimal amount, Currency currency, PaymentMethod method) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Create(CultureInfo.InvariantCulture, $"{amount:F2}|{currency}|{method}"))));
}
