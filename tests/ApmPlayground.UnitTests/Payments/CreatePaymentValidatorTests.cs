using System.Globalization;
using ApmPlayground.Application.Payments;
using AutoFixture;
using FluentValidation.Results;

namespace ApmPlayground.UnitTests.Payments;

public class CreatePaymentValidatorTests
{
    private readonly Fixture _fixture = new();
    private readonly CreatePaymentValidator _validator = new();

    [Theory]
    [InlineData("ideal", "EUR", "10.50")]
    [InlineData("klarna", "GBP", "0.01")]
    [InlineData("klarna", "USD", "1000")]
    [InlineData("ideal", "EUR", "9999999999999999.99")]
    public void Validate_ValidRequest_ReturnsNoErrors(string method, string currency, string amount)
    {
        var request = new CreatePaymentRequest(ParseAmount(amount), currency, method);

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

    [Fact]
    public void Validate_AmountMissing_ReturnsAmountError()
    {
        var request = ValidRequest() with { Amount = null };

        var result = _validator.Validate(request);

        AssertSingleError(result, "amount", "Amount is required.");
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

    [Theory]
    [InlineData("JPY")]
    [InlineData("eur")]
    [InlineData("EURO")]
    [InlineData(" ")]
    public void Validate_UnsupportedCurrency_ReturnsCurrencyError(string currency)
    {
        var request = ValidRequest() with { Currency = currency };

        var result = _validator.Validate(request);

        AssertSingleError(result, "currency", $"Currency '{currency}' is not supported.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Validate_CurrencyMissing_ReturnsCurrencyError(string? currency)
    {
        var request = ValidRequest() with { Currency = currency };

        var result = _validator.Validate(request);

        AssertSingleError(result, "currency", "Currency is required.");
    }

    [Theory]
    [InlineData("paypal")]
    [InlineData("IDEAL")]
    [InlineData("Klarna")]
    [InlineData(" ")]
    public void Validate_UnknownMethod_ReturnsMethodError(string method)
    {
        var request = ValidRequest() with { Method = method };

        var result = _validator.Validate(request);

        AssertSingleError(result, "method", "Method must be 'ideal' or 'klarna'.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Validate_MethodMissing_ReturnsMethodError(string? method)
    {
        var request = ValidRequest() with { Method = method };

        var result = _validator.Validate(request);

        AssertSingleError(result, "method", "Method is required.");
    }

    [Fact]
    public void Validate_AllFieldsInvalid_ReturnsErrorForEachField()
    {
        var currency = _fixture.Create<string>();
        var request = new CreatePaymentRequest(-_fixture.Create<decimal>(), currency, _fixture.Create<string>());

        var result = _validator.Validate(request);

        var errors = result.ToDictionary();
        Assert.Multiple(
            () => Assert.Equal(3, errors.Count),
            () => Assert.Equal("Amount must be greater than 0.", Assert.Single(errors["amount"])),
            () => Assert.Equal($"Currency '{currency}' is not supported.", Assert.Single(errors["currency"])),
            () => Assert.Equal("Method must be 'ideal' or 'klarna'.", Assert.Single(errors["method"])));
    }

    private CreatePaymentRequest ValidRequest()
    {
        string[] methods = ["ideal", "klarna"];
        string[] currencies = ["EUR", "GBP", "USD"];
        return new CreatePaymentRequest(
            _fixture.Create<int>() + 0.99m,
            currencies[_fixture.Create<int>() % currencies.Length],
            methods[_fixture.Create<int>() % methods.Length]);
    }

    private static decimal ParseAmount(string amount) => decimal.Parse(amount, CultureInfo.InvariantCulture);

    private static void AssertSingleError(ValidationResult result, string key, string message)
    {
        var error = Assert.Single(result.Errors);
        Assert.Multiple(
            () => Assert.Equal(key, error.PropertyName),
            () => Assert.Equal(message, error.ErrorMessage));
    }
}
