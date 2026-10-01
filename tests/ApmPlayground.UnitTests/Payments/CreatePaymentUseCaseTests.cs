using ApmPlayground.Application.Payments;
using ApmPlayground.Domain.Payments;
using AutoFixture;

namespace ApmPlayground.UnitTests.Payments;

public sealed class CreatePaymentUseCaseTests
{
    private readonly Fixture _fixture = new();
    private readonly FakePaymentRepository _repository = new();
    private readonly CreatePaymentUseCase _useCase;

    public CreatePaymentUseCaseTests()
    {
        _useCase = new CreatePaymentUseCase(new CreatePaymentValidator(), _repository);
    }

    [Theory]
    [InlineData(PaymentMethod.Ideal, "ideal")]
    [InlineData(PaymentMethod.Klarna, "klarna")]
    public async Task ExecuteAsync_ValidRequest_ReturnsPendingResponseWithRedirectUrl(PaymentMethod method, string segment)
    {
        var request = ValidRequest() with { Method = method };

        var result = await _useCase.ExecuteAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Multiple(
            () => Assert.NotEqual(Guid.Empty, result.Response.PaymentId),
            () => Assert.Equal(PaymentStatus.Pending, result.Response.Status),
            () => Assert.Equal(
                new Uri($"https://pay.example.com/{segment}/{result.Response.PaymentId:D}"),
                result.Response.RedirectUrl),
            () => Assert.Empty(result.Errors));
    }

    [Fact]
    public async Task ExecuteAsync_ValidRequest_AddsPaymentToRepository()
    {
        var request = ValidRequest() with { Method = PaymentMethod.Klarna };

        var result = await _useCase.ExecuteAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var payment = Assert.Single(_repository.Added);
        Assert.Multiple(
            () => Assert.Equal(result.Response.PaymentId, payment.Id),
            () => Assert.Equal(request.Amount, payment.Amount),
            () => Assert.Equal(request.Currency, payment.Currency),
            () => Assert.Equal(PaymentMethod.Klarna, payment.Method),
            () => Assert.Equal(PaymentStatus.Pending, payment.Status));
    }

    [Fact]
    public async Task ExecuteAsync_InvalidRequest_ReturnsErrorsAndDoesNotAddPayment()
    {
        var request = ValidRequest() with { Amount = -_fixture.Create<int>() };

        var result = await _useCase.ExecuteAsync(request, CancellationToken.None);

        Assert.Multiple(
            () => Assert.False(result.IsSuccess),
            () => Assert.Null(result.Response),
            () => Assert.Equal("amount", Assert.Single(result.Errors).Key),
            () => Assert.Equal("Amount must be greater than 0.", Assert.Single(result.Errors["amount"])),
            () => Assert.Empty(_repository.Added));
    }

    [Fact]
    public async Task ExecuteAsync_UndefinedMethod_ReturnsErrorsAndDoesNotAddPayment()
    {
        var request = ValidRequest() with { Method = (PaymentMethod)99 };

        var result = await _useCase.ExecuteAsync(request, CancellationToken.None);

        Assert.Multiple(
            () => Assert.False(result.IsSuccess),
            () => Assert.Null(result.Response),
            () => Assert.Equal("method", Assert.Single(result.Errors).Key),
            () => Assert.Equal("Method has an invalid value.", Assert.Single(result.Errors["method"])),
            () => Assert.Empty(_repository.Added));
    }

    private CreatePaymentRequest ValidRequest() => new()
    {
        Amount = _fixture.Create<int>() + 0.99m,
        Currency = _fixture.Create<Currency>(),
        Method = _fixture.Create<PaymentMethod>(),
    };
}
