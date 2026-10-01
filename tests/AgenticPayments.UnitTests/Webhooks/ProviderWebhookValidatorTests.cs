using AgenticPayments.Application.Webhooks;
using AutoFixture;
using FluentValidation.Results;

namespace AgenticPayments.UnitTests.Webhooks;

public sealed class ProviderWebhookValidatorTests
{
    private readonly Fixture _fixture = new();
    private readonly ProviderWebhookValidator _validator = new();

    [Theory]
    [InlineData(1, ProviderPaymentStatus.Succeeded)]
    [InlineData(1, ProviderPaymentStatus.Failed)]
    [InlineData(200, ProviderPaymentStatus.Succeeded)]
    [InlineData(200, ProviderPaymentStatus.Failed)]
    public void Validate_ValidRequest_ReturnsNoErrors(int eventIdLength, ProviderPaymentStatus status)
    {
        var request = ValidRequest() with { EventId = new string('e', eventIdLength), Status = status };

        var result = _validator.Validate(request);

        Assert.Multiple(
            () => Assert.True(result.IsValid),
            () => Assert.Empty(result.Errors));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public void Validate_EventIdEmpty_ReturnsEventIdError(string? eventId)
    {
        // A JSON null passes the required-member check of the reader, so the validator must reject it.
        var request = ValidRequest() with { EventId = eventId! };

        var result = _validator.Validate(request);

        AssertSingleError(result, "eventId", "EventId is required.");
    }

    [Fact]
    public void Validate_EventIdTooLong_ReturnsEventIdError()
    {
        var request = ValidRequest() with { EventId = new string('e', 201) };

        var result = _validator.Validate(request);

        AssertSingleError(result, "eventId", "EventId must be at most 200 characters.");
    }

    [Fact]
    public void Validate_PaymentIdEmpty_ReturnsPaymentIdError()
    {
        var request = ValidRequest() with { PaymentId = Guid.Empty };

        var result = _validator.Validate(request);

        AssertSingleError(result, "paymentId", "PaymentId is required.");
    }

    [Fact]
    public void Validate_StatusUndefined_ReturnsStatusError()
    {
        var request = ValidRequest() with { Status = (ProviderPaymentStatus)99 };

        var result = _validator.Validate(request);

        AssertSingleError(result, "status", "Status has an invalid value.");
    }

    private ProviderWebhookRequest ValidRequest() => new()
    {
        EventId = $"evt_{_fixture.Create<Guid>():N}",
        PaymentId = _fixture.Create<Guid>(),
        Status = _fixture.Create<ProviderPaymentStatus>(),
    };

    private static void AssertSingleError(ValidationResult result, string key, string message)
    {
        var error = Assert.Single(result.Errors);
        Assert.Multiple(
            () => Assert.Equal(key, error.PropertyName),
            () => Assert.Equal(message, error.ErrorMessage));
    }
}
