using AgenticPayments.Application.Payments;
using AgenticPayments.Domain.Payments;
using AutoFixture;

namespace AgenticPayments.UnitTests.Payments;

public sealed class CreatePaymentUseCaseTests
{
    private const string KeyError = "Idempotency-Key";
    private const string KeyRequiredMessage = "Idempotency-Key header is required.";
    private const string KeyTooLongMessage = "Idempotency-Key header must be at most 100 characters.";

    private readonly Fixture _fixture = new();
    private readonly FakeIdempotencyRecordRepository _records = new();
    private readonly FakeTimeProvider _timeProvider;
    private readonly CreatePaymentUseCase _useCase;

    public CreatePaymentUseCaseTests()
    {
        _timeProvider = new FakeTimeProvider(_fixture.Create<DateTimeOffset>());
        _useCase = new CreatePaymentUseCase(new CreatePaymentValidator(), new IdempotencyKeyValidator(), _records, _timeProvider);
    }

    public static TheoryData<string, string> InvalidKeys => new()
    {
        { string.Empty, KeyRequiredMessage },
        { " ", KeyRequiredMessage },
        { new string('k', 101), KeyTooLongMessage },
    };

    [Theory]
    [InlineData(PaymentMethod.Ideal, "ideal")]
    [InlineData(PaymentMethod.Klarna, "klarna")]
    public async Task ExecuteAsync_ValidRequest_ReturnsPendingResponseWithRedirectUrl(PaymentMethod method, string segment)
    {
        var request = ValidRequest() with { Method = method };

        var result = await _useCase.ExecuteAsync(NewKey(), request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Multiple(
            () => Assert.Equal(CreatePaymentOutcome.Success, result.Outcome),
            () => Assert.NotEqual(Guid.Empty, result.Response.PaymentId),
            () => Assert.Equal(PaymentStatus.Pending, result.Response.Status),
            () => Assert.Equal(
                new Uri($"https://pay.example.com/{segment}/{result.Response.PaymentId:D}"),
                result.Response.RedirectUrl),
            () => Assert.Empty(result.Errors));
    }

    [Fact]
    public async Task ExecuteAsync_NewKey_SavesPaymentWithRecord()
    {
        var key = NewKey();
        var request = ValidRequest();

        var result = await _useCase.ExecuteAsync(key, request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var (record, payment) = Assert.Single(_records.Saved);
        Assert.Multiple(
            () => Assert.Equal(result.Response.PaymentId, payment.Id),
            () => Assert.Equal(request.Amount, payment.Amount),
            () => Assert.Equal(request.Currency, payment.Currency),
            () => Assert.Equal(request.Method, payment.Method),
            () => Assert.Equal(PaymentStatus.Pending, payment.Status),
            () => Assert.Equal(key, record.Key),
            () => Assert.Equal(CreatePaymentRequestHash.Compute(request.Amount, request.Currency, request.Method), record.RequestHash),
            () => Assert.Equal(payment.Id, record.PaymentId),
            () => Assert.Equal(_timeProvider.UtcNow, record.CreatedAt),
            () => Assert.Equal(1, _records.FindCalls));
    }

    [Fact]
    public async Task ExecuteAsync_SameKeySameRequest_ReturnsFirstResponseAndSavesNothing()
    {
        var key = NewKey();
        var request = ValidRequest();
        var first = await _useCase.ExecuteAsync(key, request, CancellationToken.None);
        _timeProvider.UtcNow += TimeSpan.FromHours(1);

        var second = await _useCase.ExecuteAsync(key, request with { }, CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Multiple(
            () => Assert.Equal(CreatePaymentOutcome.Success, second.Outcome),
            () => Assert.Equal(first.Response.PaymentId, second.Response.PaymentId),
            () => Assert.Equal(first.Response.RedirectUrl, second.Response.RedirectUrl),
            () => Assert.Equal(PaymentStatus.Pending, second.Response.Status),
            () => Assert.Single(_records.Saved),
            () => Assert.Equal(1, _records.SaveCalls));
    }

    [Theory]
    [InlineData("amount")]
    [InlineData("currency")]
    [InlineData("method")]
    public async Task ExecuteAsync_SameKeyDifferentRequest_ReturnsIdempotencyKeyReusedAndSavesNothing(string difference)
    {
        var key = NewKey();
        var request = ValidRequest();
        await _useCase.ExecuteAsync(key, request, CancellationToken.None);
        var (storedRecord, _) = Assert.Single(_records.Saved);
        var storedHash = storedRecord.RequestHash;
        var storedPaymentId = storedRecord.PaymentId;
        var otherRequest = difference switch
        {
            "amount" => request with { Amount = request.Amount + 1 },
            "currency" => request with { Currency = Enum.GetValues<Currency>().First(c => c != request.Currency) },
            "method" => request with { Method = Enum.GetValues<PaymentMethod>().First(m => m != request.Method) },
            _ => throw new ArgumentOutOfRangeException(nameof(difference), difference, null),
        };

        var result = await _useCase.ExecuteAsync(key, otherRequest, CancellationToken.None);

        Assert.Multiple(
            () => Assert.Equal(CreatePaymentOutcome.IdempotencyKeyReused, result.Outcome),
            () => Assert.False(result.IsSuccess),
            () => Assert.Null(result.Response),
            () => Assert.Empty(result.Errors),
            () => Assert.Single(_records.Saved),
            () => Assert.Equal(1, _records.SaveCalls),
            () => Assert.Equal(storedHash, storedRecord.RequestHash),
            () => Assert.Equal(storedPaymentId, storedRecord.PaymentId));
    }

    [Theory]
    [MemberData(nameof(InvalidKeys))]
    public async Task ExecuteAsync_InvalidKey_ReturnsInvalidAndDoesNotTouchRepository(string key, string expectedMessage)
    {
        var result = await _useCase.ExecuteAsync(key, ValidRequest(), CancellationToken.None);

        Assert.Multiple(
            () => Assert.Equal(CreatePaymentOutcome.Invalid, result.Outcome),
            () => Assert.False(result.IsSuccess),
            () => Assert.Null(result.Response),
            () => Assert.Equal(KeyError, Assert.Single(result.Errors).Key),
            () => Assert.Equal(expectedMessage, Assert.Single(result.Errors[KeyError])),
            () => Assert.Equal(0, _records.FindCalls),
            () => Assert.Equal(0, _records.SaveCalls));
    }

    [Fact]
    public async Task ExecuteAsync_InvalidKeyAndInvalidRequest_ReturnsBothErrors()
    {
        var request = ValidRequest() with { Amount = -_fixture.Create<int>() };

        var result = await _useCase.ExecuteAsync(string.Empty, request, CancellationToken.None);

        Assert.Multiple(
            () => Assert.Equal(CreatePaymentOutcome.Invalid, result.Outcome),
            () => Assert.Equal(2, result.Errors.Count),
            () => Assert.Equal(KeyRequiredMessage, Assert.Single(result.Errors[KeyError])),
            () => Assert.Equal("Amount must be greater than 0.", Assert.Single(result.Errors["amount"])),
            () => Assert.Equal(0, _records.FindCalls),
            () => Assert.Equal(0, _records.SaveCalls));
    }

    [Fact]
    public async Task ExecuteAsync_InvalidRequest_ReturnsErrorsAndSavesNothing()
    {
        var request = ValidRequest() with { Amount = -_fixture.Create<int>() };

        var result = await _useCase.ExecuteAsync(NewKey(), request, CancellationToken.None);

        Assert.Multiple(
            () => Assert.Equal(CreatePaymentOutcome.Invalid, result.Outcome),
            () => Assert.False(result.IsSuccess),
            () => Assert.Null(result.Response),
            () => Assert.Equal("amount", Assert.Single(result.Errors).Key),
            () => Assert.Equal("Amount must be greater than 0.", Assert.Single(result.Errors["amount"])),
            () => Assert.Equal(0, _records.FindCalls),
            () => Assert.Equal(0, _records.SaveCalls));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteAsync_ExpiredKey_RenewsRecordAndCreatesNewPayment(bool differentRequest)
    {
        var key = NewKey();
        var firstRequest = ValidRequest();
        var oldPaymentId = _fixture.Create<Guid>();
        var expiredRecord = new IdempotencyRecord(
            key,
            CreatePaymentRequestHash.Compute(firstRequest.Amount, firstRequest.Currency, firstRequest.Method),
            oldPaymentId,
            _timeProvider.UtcNow - IdempotencyRecord.Lifetime - TimeSpan.FromMinutes(1));
        _records.Stored.Add(key, expiredRecord);
        var request = differentRequest ? firstRequest with { Amount = firstRequest.Amount + 1 } : firstRequest;

        var result = await _useCase.ExecuteAsync(key, request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var (record, payment) = Assert.Single(_records.Saved);
        Assert.Multiple(
            () => Assert.NotEqual(oldPaymentId, result.Response.PaymentId),
            () => Assert.Equal(payment.Id, result.Response.PaymentId),
            () => Assert.Equal(request.Amount, payment.Amount),
            () => Assert.Same(expiredRecord, record),
            () => Assert.Equal(key, record.Key),
            () => Assert.Equal(payment.Id, record.PaymentId),
            () => Assert.Equal(CreatePaymentRequestHash.Compute(request.Amount, request.Currency, request.Method), record.RequestHash),
            () => Assert.Equal(_timeProvider.UtcNow, record.CreatedAt),
            () => Assert.Equal(1, _records.SaveCalls));
    }

    [Fact]
    public async Task ExecuteAsync_KeyJustBeforeExpiry_ReplaysFirstResponse()
    {
        var key = NewKey();
        var request = ValidRequest() with { Method = PaymentMethod.Klarna };
        var paymentId = _fixture.Create<Guid>();
        _records.Stored.Add(key, new IdempotencyRecord(
            key,
            CreatePaymentRequestHash.Compute(request.Amount, request.Currency, request.Method),
            paymentId,
            _timeProvider.UtcNow - IdempotencyRecord.Lifetime + TimeSpan.FromTicks(1)));

        var result = await _useCase.ExecuteAsync(key, request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Multiple(
            () => Assert.Equal(paymentId, result.Response.PaymentId),
            () => Assert.Equal(new Uri($"https://pay.example.com/klarna/{paymentId:D}"), result.Response.RedirectUrl),
            () => Assert.Equal(PaymentStatus.Pending, result.Response.Status),
            () => Assert.Equal(0, _records.SaveCalls));
    }

    [Fact]
    public async Task ExecuteAsync_SaveReturnsKeyConflict_ReturnsIdempotencyKeyInProgress()
    {
        _records.SaveResults.Enqueue(IdempotencySaveResult.KeyConflict);

        var result = await _useCase.ExecuteAsync(NewKey(), ValidRequest(), CancellationToken.None);

        Assert.Multiple(
            () => Assert.Equal(CreatePaymentOutcome.IdempotencyKeyInProgress, result.Outcome),
            () => Assert.False(result.IsSuccess),
            () => Assert.Null(result.Response),
            () => Assert.Empty(result.Errors),
            () => Assert.Empty(_records.Saved),
            () => Assert.Equal(1, _records.SaveCalls));
    }

    private string NewKey() => _fixture.Create<Guid>().ToString();

    private CreatePaymentRequest ValidRequest() => new()
    {
        Amount = _fixture.Create<int>() + 0.99m,
        Currency = _fixture.Create<Currency>(),
        Method = _fixture.Create<PaymentMethod>(),
    };
}
