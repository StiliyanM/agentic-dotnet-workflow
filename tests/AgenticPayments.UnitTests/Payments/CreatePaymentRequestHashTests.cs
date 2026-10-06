using System.Security.Cryptography;
using System.Text;
using AgenticPayments.Application.Payments;
using AgenticPayments.Domain.Payments;
using AutoFixture;

namespace AgenticPayments.UnitTests.Payments;

public sealed class CreatePaymentRequestHashTests
{
    private readonly Fixture _fixture = new();

    [Fact]
    public void Compute_KnownRequest_ReturnsLowercaseSha256OfCanonicalString()
    {
        var expected = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("10.50|Eur|Ideal")));

        var hash = CreatePaymentRequestHash.Compute(10.50m, Currency.Eur, PaymentMethod.Ideal);

        Assert.Multiple(
            () => Assert.Equal(expected, hash),
            () => Assert.Equal(64, hash.Length));
    }

    [Fact]
    public void Compute_SameAmountOtherScale_ReturnsSameHash()
    {
        var currency = _fixture.Create<Currency>();
        var method = _fixture.Create<PaymentMethod>();

        var shortScale = CreatePaymentRequestHash.Compute(10.5m, currency, method);
        var longScale = CreatePaymentRequestHash.Compute(10.50m, currency, method);

        Assert.Equal(longScale, shortScale);
    }

    [Theory]
    [InlineData("amount")]
    [InlineData("currency")]
    [InlineData("method")]
    public void Compute_DifferentAmountCurrencyOrMethod_ReturnsDifferentHash(string difference)
    {
        var amount = _fixture.Create<int>() + 0.25m;
        var hash = CreatePaymentRequestHash.Compute(amount, Currency.Eur, PaymentMethod.Ideal);

        var otherHash = difference switch
        {
            "amount" => CreatePaymentRequestHash.Compute(amount + 0.01m, Currency.Eur, PaymentMethod.Ideal),
            "currency" => CreatePaymentRequestHash.Compute(amount, Currency.Usd, PaymentMethod.Ideal),
            "method" => CreatePaymentRequestHash.Compute(amount, Currency.Eur, PaymentMethod.Klarna),
            _ => throw new ArgumentOutOfRangeException(nameof(difference), difference, null),
        };

        Assert.NotEqual(hash, otherHash);
    }
}
