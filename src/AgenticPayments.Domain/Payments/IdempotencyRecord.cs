namespace AgenticPayments.Domain.Payments;

// Links a client Idempotency-Key to the payment that its first request created, for Lifetime after CreatedAt.
public sealed class IdempotencyRecord(string key, string requestHash, Guid paymentId, DateTimeOffset createdAt)
{
    public const int MaxKeyLength = 100;
    public const int RequestHashLength = 64;
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);

    public string Key { get; private set; } = NotEmpty(key, nameof(key));

    public string RequestHash { get; private set; } = NotEmpty(requestHash, nameof(requestHash));

    public Guid PaymentId { get; private set; } = paymentId;

    public DateTimeOffset CreatedAt { get; private set; } = createdAt;

    public bool IsExpired(DateTimeOffset now) => now >= CreatedAt + Lifetime;

    // A live key is never reassigned, so a replay always returns the payment of the first request.
    public void Renew(string requestHash, Guid paymentId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrEmpty(requestHash);
        if (!IsExpired(now))
        {
            throw new InvalidOperationException("Only an expired idempotency key can be renewed.");
        }

        RequestHash = requestHash;
        PaymentId = paymentId;
        CreatedAt = now;
    }

    private static string NotEmpty(string value, string paramName)
    {
        ArgumentException.ThrowIfNullOrEmpty(value, paramName);
        return value;
    }
}
