using ApmPlayground.Application.Payments;
using ApmPlayground.Domain.Payments;
using AutoFixture;

namespace ApmPlayground.UnitTests.Payments;

public class CreatePaymentUseCaseTests
{
    private readonly Fixture _fixture = new();
    private readonly FakePaymentRepository _repository = new();
    private readonly CreatePaymentUseCase _useCase;

    public CreatePaymentUseCaseTests()
    {
        _useCase = new CreatePaymentUseCase(new CreatePaymentValidator(), _repository);
    }

    [Theory]
    [InlineData("ideal")]
    [InlineData("klarna")]
    public async Task ExecuteAsync_ValidRequest_ReturnsPendingResponseWithRedirectUrl(string method)
    {
        var request = ValidRequest() with { Method = method };

        var result = await _useCase.ExecuteAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Multiple(
            () => Assert.NotEqual(Guid.Empty, result.Response.PaymentId),
            () => Assert.Equal("Pending", result.Response.Status),
            () => Assert.Equal($"https://pay.example.com/{method}/{result.Response.PaymentId:D}", result.Response.RedirectUrl),
            () => Assert.Empty(result.Errors));
    }

    [Fact]
    public async Task ExecuteAsync_ValidRequest_AddsPaymentToRepository()
    {
        var request = ValidRequest() with { Method = "klarna" };

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

    private CreatePaymentRequest ValidRequest()
    {
        string[] methods = ["ideal", "klarna"];
        string[] currencies = ["EUR", "GBP", "USD"];
        return new CreatePaymentRequest(
            _fixture.Create<int>() + 0.99m,
            currencies[_fixture.Create<int>() % currencies.Length],
            methods[_fixture.Create<int>() % methods.Length]);
    }
}
