using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Primitives;

namespace AgenticPayments.Api.Webhooks;

// Checks "X-Provider-Signature: sha256=<hex of HMAC-SHA256 over the raw body bytes>".
// The body is buffered and rewound, so the endpoint can read it again after the check.
public static class ProviderWebhookSignature
{
    public const string HeaderName = "X-Provider-Signature";
    public const string Prefix = "sha256=";

    public static async Task<bool> IsValidAsync(HttpRequest request, string secret, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrEmpty(secret);

        request.EnableBuffering();
        var hash = await HMACSHA256.HashDataAsync(Encoding.UTF8.GetBytes(secret), request.Body, cancellationToken);
        request.Body.Position = 0;

        return DecodeSignature(request.Headers[HeaderName]) is { } signature
            && CryptographicOperations.FixedTimeEquals(hash, signature);
    }

    private static byte[]? DecodeSignature(StringValues values)
    {
        if (values.Count != 1 || values[0] is not { } value || !value.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var signature = new byte[HMACSHA256.HashSizeInBytes];
        var status = Convert.FromHexString(value.AsSpan(Prefix.Length), signature, out _, out var bytesWritten);
        return status == OperationStatus.Done && bytesWritten == signature.Length ? signature : null;
    }
}
