using AgenticPayments.Domain.Payments;
using AutoFixture;

namespace AgenticPayments.UnitTests.Payments;

public sealed class IdempotencyRecordTests
{
    private readonly Fixture _fixture = new();

    [Theory]
    [InlineData("", "hash")]
    [InlineData("key", "")]
    public void Constructor_EmptyKeyOrHash_ThrowsArgumentException(string key, string requestHash) =>
        Assert.Throws<ArgumentException>(
            () => new IdempotencyRecord(key, requestHash, _fixture.Create<Guid>(), _fixture.Create<DateTimeOffset>()));

    [Fact]
    public void IsExpired_BeforeLifetime_ReturnsFalse()
    {
        var record = NewRecord();

        var expired = record.IsExpired(record.CreatedAt + IdempotencyRecord.Lifetime - TimeSpan.FromTicks(1));

        Assert.False(expired);
    }

    [Theory]
    [InlineData(24)]
    [InlineData(25)]
    public void IsExpired_AtOrAfterLifetime_ReturnsTrue(int hours)
    {
        var record = NewRecord();

        var expired = record.IsExpired(record.CreatedAt + TimeSpan.FromHours(hours));

        Assert.True(expired);
    }

    [Fact]
    public void Renew_Expired_SetsHashPaymentIdAndCreatedAt()
    {
        var record = NewRecord();
        var key = record.Key;
        var newHash = _fixture.Create<string>();
        var newPaymentId = _fixture.Create<Guid>();
        var now = record.CreatedAt + IdempotencyRecord.Lifetime + TimeSpan.FromMinutes(1);

        record.Renew(newHash, newPaymentId, now);

        Assert.Multiple(
            () => Assert.Equal(key, record.Key),
            () => Assert.Equal(newHash, record.RequestHash),
            () => Assert.Equal(newPaymentId, record.PaymentId),
            () => Assert.Equal(now, record.CreatedAt),
            () => Assert.False(record.IsExpired(now)));
    }

    [Fact]
    public void Renew_NotExpired_ThrowsInvalidOperationException()
    {
        var record = NewRecord();
        var hash = record.RequestHash;
        var paymentId = record.PaymentId;
        var createdAt = record.CreatedAt;
        var now = createdAt + IdempotencyRecord.Lifetime - TimeSpan.FromTicks(1);

        var exception = Record.Exception(() => record.Renew(_fixture.Create<string>(), _fixture.Create<Guid>(), now));

        Assert.Multiple(
            () => Assert.IsType<InvalidOperationException>(exception),
            () => Assert.Equal(hash, record.RequestHash),
            () => Assert.Equal(paymentId, record.PaymentId),
            () => Assert.Equal(createdAt, record.CreatedAt));
    }

    private IdempotencyRecord NewRecord() => new(
        _fixture.Create<string>(),
        _fixture.Create<string>(),
        _fixture.Create<Guid>(),
        _fixture.Create<DateTimeOffset>());
}
