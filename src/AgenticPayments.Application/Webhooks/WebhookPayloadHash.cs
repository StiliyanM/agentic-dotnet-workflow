using System.Security.Cryptography;
using System.Text;

namespace AgenticPayments.Application.Webhooks;

// Hashes the meaning of the payload (paymentId and status), so JSON formatting and status casing do not change it.
public static class WebhookPayloadHash
{
    public static string Compute(Guid paymentId, ProviderPaymentStatus status) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{paymentId:D}|{status}")));
}
