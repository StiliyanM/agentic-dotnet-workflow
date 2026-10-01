using AgenticPayments.Domain.Payments;
using AutoFixture;

namespace AgenticPayments.UnitTests.Payments;

public sealed class PaymentTests
{
    private readonly Fixture _fixture = new();

    [Fact]
    public void Constructor_SetsPendingStatusAndNewId()
    {
        var amount = _fixture.Create<decimal>();
        var currency = _fixture.Create<Currency>();
        var method = _fixture.Create<PaymentMethod>();
        var before = DateTimeOffset.UtcNow;

        var payment = new Payment(amount, currency, method);

        var after = DateTimeOffset.UtcNow;
        Assert.Multiple(
            () => Assert.NotEqual(Guid.Empty, payment.Id),
            () => Assert.Equal(PaymentStatus.Pending, payment.Status),
            () => Assert.Equal(amount, payment.Amount),
            () => Assert.Equal(currency, payment.Currency),
            () => Assert.Equal(method, payment.Method),
            () => Assert.InRange(payment.CreatedAt, before, after));
    }

    [Fact]
    public void Constructor_TwoPayments_HaveDifferentIds()
    {
        var method = _fixture.Create<PaymentMethod>();

        var first = new Payment(_fixture.Create<decimal>(), _fixture.Create<Currency>(), method);
        var second = new Payment(_fixture.Create<decimal>(), _fixture.Create<Currency>(), method);

        Assert.NotEqual(first.Id, second.Id);
    }

    [Theory]
    [InlineData(PaymentStatus.Succeeded)]
    [InlineData(PaymentStatus.Failed)]
    public void ChangeStatus_SetsStatus(PaymentStatus status)
    {
        var payment = new Payment(_fixture.Create<decimal>(), _fixture.Create<Currency>(), _fixture.Create<PaymentMethod>());

        payment.ChangeStatus(status);

        Assert.Equal(status, payment.Status);
    }
}
