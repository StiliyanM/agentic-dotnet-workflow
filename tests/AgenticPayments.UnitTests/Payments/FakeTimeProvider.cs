namespace AgenticPayments.UnitTests.Payments;

// A clock that the test sets, so that the expiry of an idempotency key does not depend on the real time.
public sealed class FakeTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = utcNow;

    public override DateTimeOffset GetUtcNow() => UtcNow;
}
