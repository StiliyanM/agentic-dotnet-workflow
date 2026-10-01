using System.Globalization;
using ApmPlayground.Application.Payments;
using ApmPlayground.Domain.Payments;
using AutoFixture;
using FluentValidation.Results;

namespace ApmPlayground.UnitTests.Payments;

public sealed class CreatePaymentValidatorTests
{
    private readonly Fixture _fixture = new();
    private readonly CreatePaymentValidator _validator = new();

    [Theory]
    [InlineData(PaymentMethod.Ideal, Currency.Eur, "10.50")]
    [InlineData(PaymentMethod.Klarna, Currency.Gbp, "0.01")]
    [InlineData(PaymentMethod.Klarna, Currency.Usd, "1000")]
    [InlineData(PaymentMethod.Ideal, Currency.Eur, "9999999999999999.99")]
    public void Validate_ValidRequest_ReturnsNoErrors(PaymentMethod method, Currency currency, string amount)
    {
        var request = new CreatePaymentRequest
        {
            Amount = ParseAmount(amount),
            Currency = currency,
            Method = method,
        };

        var result = _validator.Validate(request);

        Assert.Multiple(
            () => Assert.True(result.IsValid),
            () => Assert.Empty(result.Errors));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-0.01")]
    [InlineData("-100")]
    public void Validate_AmountNotPositive_ReturnsAmountError(string amount)
    {
        var request = ValidRequest() with { Amount = ParseAmount(amount) };

        var result = _validator.Validate(request);

        AssertSingleError(result, "amount", "Amount must be greater than 0.");
    }

    [Fact]
    public void Validate_AmountAboveMaximum_ReturnsAmountError()
    {
        var request = ValidRequest() with { Amount = 10000000000000000m };

        var result = _validator.Validate(request);

        AssertSingleError(result, "amount", "Amount is too large.");
    }

    [Theory]
    [InlineData("0.001")]
    [InlineData("10.123")]
    public void Validate_AmountWithMoreThanTwoDecimals_ReturnsAmountError(string amount)
    {
        var request = ValidRequest() with { Amount = ParseAmount(amount) };

        var result = _validator.Validate(request);

        AssertSingleError(result, "amount", "Amount must have at most 2 decimal places.");
    }

    [Fact]
    public void Validate_UndefinedCurrency_ReturnsCurrencyError()
    {
        var request = ValidRequest() with { Currency = (Currency)99 };

        var result = _validator.Validate(request);

        AssertSingleError(result, "currency", "Currency has an invalid value.");
    }

    [Fact]
    public void Validate_UndefinedMethod_ReturnsMethodError()
    {
        var request = ValidRequest() with { Method = (PaymentMethod)99 };

        var result = _validator.Validate(request);

        AssertSingleError(result, "method", "Method has an invalid value.");
    }

    private CreatePaymentRequest ValidRequest() => new()
    {
        Amount = _fixture.Create<int>() + 0.99m,
        Currency = _fixture.Create<Currency>(),
        Method = _fixture.Create<PaymentMethod>(),
    };

    private static decimal ParseAmount(string amount) => decimal.Parse(amount, CultureInfo.InvariantCulture);

    private static void AssertSingleError(ValidationResult result, string key, string message)
    {
        var error = Assert.Single(result.Errors);
        Assert.Multiple(
            () => Assert.Equal(key, error.PropertyName),
            () => Assert.Equal(message, error.ErrorMessage));
    }
}
