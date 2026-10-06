using AgenticPayments.Application.Payments;
using AgenticPayments.Domain.Payments;
using AutoFixture;
using FluentValidation.Results;

namespace AgenticPayments.UnitTests.Payments;

public sealed class IdempotencyKeyValidatorTests
{
    private readonly Fixture _fixture = new();
    private readonly IdempotencyKeyValidator _validator = new();

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   ")]
    public void Validate_EmptyOrWhitespace_ReturnsRequiredError(string key)
    {
        var result = _validator.Validate(key);

        AssertSingleError(result, "Idempotency-Key header is required.");
    }

    [Fact]
    public void Validate_LongerThan100_ReturnsLengthError()
    {
        var key = new string('k', IdempotencyRecord.MaxKeyLength + 1);

        var result = _validator.Validate(key);

        AssertSingleError(result, "Idempotency-Key header must be at most 100 characters.");
    }

    [Theory]
    [InlineData("one character")]
    [InlineData("100 characters")]
    [InlineData("uuid")]
    [InlineData("mixed case")]
    public void Validate_1To100Characters_IsValid(string kind)
    {
        var key = kind switch
        {
            "one character" => "k",
            "100 characters" => new string('k', 100),
            "uuid" => _fixture.Create<Guid>().ToString(),
            "mixed case" => $"Order-{_fixture.Create<int>()}-AbC",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };

        var result = _validator.Validate(key);

        Assert.Multiple(
            () => Assert.True(result.IsValid),
            () => Assert.Empty(result.Errors));
    }

    private static void AssertSingleError(ValidationResult result, string message)
    {
        var error = Assert.Single(result.Errors);
        Assert.Multiple(
            () => Assert.Equal("Idempotency-Key", error.PropertyName),
            () => Assert.Equal(message, error.ErrorMessage));
    }
}
