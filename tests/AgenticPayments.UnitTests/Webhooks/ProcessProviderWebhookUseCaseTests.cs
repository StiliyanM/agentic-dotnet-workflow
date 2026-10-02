using AgenticPayments.Application.Webhooks;
using AgenticPayments.Domain.Payments;
using AgenticPayments.Domain.Webhooks;
using AgenticPayments.UnitTests.Payments;
using AutoFixture;

namespace AgenticPayments.UnitTests.Webhooks;

public sealed class ProcessProviderWebhookUseCaseTests
{
    private readonly Fixture _fixture = new();
    private readonly FakePaymentRepository _payments = new();
    private readonly FakeWebhookEventRepository _webhookEvents = new();
    private readonly ProcessProviderWebhookUseCase _useCase;

    public ProcessProviderWebhookUseCaseTests()
    {
        _useCase = new ProcessProviderWebhookUseCase(new ProviderWebhookValidator(), _payments, _webhookEvents);
    }

    [Theory]
    [InlineData(ProviderPaymentStatus.Succeeded, PaymentStatus.Succeeded)]
    [InlineData(ProviderPaymentStatus.Failed, PaymentStatus.Failed)]
    public async Task ExecuteAsync_PendingPayment_AppliesStatusAndRecordsEventWithHash(
        ProviderPaymentStatus status,
        PaymentStatus expectedStatus)
    {
        var payment = SeedPayment();
        var request = Request(payment.Id, status);

        var result = await _useCase.ExecuteAsync(request, CancellationToken.None);

        var recorded = Assert.Single(_webhookEvents.Recorded);
        Assert.Multiple(
            () => Assert.Equal(ProviderWebhookOutcome.Processed, result.Outcome),
            () => Assert.Empty(result.Errors),
            () => Assert.Equal(expectedStatus, payment.Status),
            () => Assert.Equal(request.EventId, recorded.Event.EventId),
            () => Assert.Equal(payment.Id, recorded.Event.PaymentId),
            () => Assert.Equal(WebhookPayloadHash.Compute(payment.Id, status), recorded.Event.PayloadHash),
            () => Assert.Same(payment, recorded.Payment));
    }

    [Theory]
    [InlineData(PaymentStatus.Succeeded, ProviderPaymentStatus.Failed)]
    [InlineData(PaymentStatus.Failed, ProviderPaymentStatus.Succeeded)]
    [InlineData(PaymentStatus.Succeeded, ProviderPaymentStatus.Succeeded)]
    [InlineData(PaymentStatus.Failed, ProviderPaymentStatus.Failed)]
    public async Task ExecuteAsync_TerminalPayment_ReturnsIgnoredKeepsStatusAndRecordsEvent(
        PaymentStatus terminalStatus,
        ProviderPaymentStatus newStatus)
    {
        var payment = SeedPayment(terminalStatus);
        var request = Request(payment.Id, newStatus);

        var result = await _useCase.ExecuteAsync(request, CancellationToken.None);

        var recorded = Assert.Single(_webhookEvents.Recorded);
        Assert.Multiple(
            () => Assert.Equal(ProviderWebhookOutcome.Ignored, result.Outcome),
            () => Assert.Empty(result.Errors),
            () => Assert.Equal(terminalStatus, payment.Status),
            () => Assert.Equal(request.EventId, recorded.Event.EventId),
            () => Assert.Equal(payment.Id, recorded.Event.PaymentId),
            () => Assert.Equal(WebhookPayloadHash.Compute(payment.Id, newStatus), recorded.Event.PayloadHash),
            () => Assert.Same(payment, recorded.Payment));
    }

    [Fact]
    public async Task ExecuteAsync_KnownEventIdSamePayload_ReturnsDuplicateAndChangesNothing()
    {
        var payment = SeedPayment();
        var request = Request(payment.Id, ProviderPaymentStatus.Succeeded);
        _webhookEvents.Stored.Add(
            new ProcessedWebhookEvent(request.EventId, payment.Id, WebhookPayloadHash.Compute(payment.Id, request.Status)));

        var result = await _useCase.ExecuteAsync(request, CancellationToken.None);

        Assert.Multiple(
            () => Assert.Equal(ProviderWebhookOutcome.Duplicate, result.Outcome),
            () => Assert.Empty(result.Errors),
            () => Assert.Equal(PaymentStatus.Pending, payment.Status),
            () => Assert.Equal(0, _webhookEvents.RecordAttempts));
    }

    [Theory]
    [InlineData("other status")]
    [InlineData("other paymentId")]
    public async Task ExecuteAsync_KnownEventIdDifferentPayload_ReturnsDuplicatePayloadMismatchAndChangesNothing(string difference)
    {
        var payment = SeedPayment();
        var request = Request(payment.Id, ProviderPaymentStatus.Succeeded);
        var storedEvent = difference switch
        {
            "other status" => new ProcessedWebhookEvent(
                request.EventId,
                payment.Id,
                WebhookPayloadHash.Compute(payment.Id, ProviderPaymentStatus.Failed)),
            "other paymentId" => StoredEventForOtherPayment(request),
            _ => throw new ArgumentOutOfRangeException(nameof(difference), difference, null),
        };
        _webhookEvents.Stored.Add(storedEvent);

        var result = await _useCase.ExecuteAsync(request, CancellationToken.None);

        Assert.Multiple(
            () => Assert.Equal(ProviderWebhookOutcome.DuplicatePayloadMismatch, result.Outcome),
            () => Assert.Empty(result.Errors),
            () => Assert.Equal(PaymentStatus.Pending, payment.Status),
            () => Assert.Equal(0, _webhookEvents.RecordAttempts));
    }

    [Theory]
    [InlineData(ProviderPaymentStatus.Succeeded)]
    [InlineData(ProviderPaymentStatus.Failed)]
    public async Task ExecuteAsync_KnownEventIdWithoutHash_ReturnsDuplicate(ProviderPaymentStatus requestStatus)
    {
        var payment = SeedPayment();
        var request = Request(payment.Id, requestStatus);
        // A row stored before spec 003 has no hash.
        _webhookEvents.Stored.Add(new ProcessedWebhookEvent(request.EventId, payment.Id, null));

        var result = await _useCase.ExecuteAsync(request, CancellationToken.None);

        Assert.Multiple(
            () => Assert.Equal(ProviderWebhookOutcome.Duplicate, result.Outcome),
            () => Assert.Empty(result.Errors),
            () => Assert.Equal(PaymentStatus.Pending, payment.Status),
            () => Assert.Equal(0, _webhookEvents.RecordAttempts));
    }

    [Fact]
    public async Task ExecuteAsync_RecordReturnsDuplicateEvent_ReturnsDuplicate()
    {
        var payment = SeedPayment();
        var request = Request(payment.Id, ProviderPaymentStatus.Failed);
        _webhookEvents.RecordResults.Enqueue(WebhookRecordResult.DuplicateEvent);

        var result = await _useCase.ExecuteAsync(request, CancellationToken.None);

        Assert.Multiple(
            () => Assert.Equal(ProviderWebhookOutcome.Duplicate, result.Outcome),
            () => Assert.Empty(result.Errors),
            () => Assert.Equal(1, _webhookEvents.RecordAttempts),
            () => Assert.Empty(_webhookEvents.Recorded));
    }

    [Fact]
    public async Task ExecuteAsync_RecordReturnsPaymentChanged_LoadsAgainAndIgnoresEvent()
    {
        var payment = SeedPayment();
        var request = Request(payment.Id, ProviderPaymentStatus.Failed);
        // Another request saved Succeeded after this request loaded the payment; a fresh load sees Succeeded.
        var reloaded = NewPayment();
        Assert.True(reloaded.TryChangeStatus(PaymentStatus.Succeeded));
        _webhookEvents.RecordResults.Enqueue(WebhookRecordResult.PaymentChanged);
        _webhookEvents.BeforeResultReturned = recordResult =>
        {
            if (recordResult == WebhookRecordResult.PaymentChanged)
            {
                _payments.Reloaded[payment.Id] = reloaded;
            }
        };

        var result = await _useCase.ExecuteAsync(request, CancellationToken.None);

        var recorded = Assert.Single(_webhookEvents.Recorded);
        Assert.Multiple(
            () => Assert.Equal(ProviderWebhookOutcome.Ignored, result.Outcome),
            () => Assert.Empty(result.Errors),
            () => Assert.Equal(2, _webhookEvents.RecordAttempts),
            () => Assert.Equal(2, _payments.FindCalls),
            () => Assert.Equal(request.EventId, recorded.Event.EventId),
            () => Assert.Equal(payment.Id, recorded.Event.PaymentId),
            () => Assert.Same(reloaded, recorded.Payment));
    }

    [Fact]
    public async Task ExecuteAsync_RecordReturnsPaymentChangedEveryTime_ThrowsInvalidOperationException()
    {
        var payment = SeedPayment();
        var request = Request(payment.Id, ProviderPaymentStatus.Succeeded);
        for (var i = 0; i <= ProcessProviderWebhookUseCase.MaxAttempts; i++)
        {
            _webhookEvents.RecordResults.Enqueue(WebhookRecordResult.PaymentChanged);
        }

        var exception = await Record.ExceptionAsync(() => _useCase.ExecuteAsync(request, CancellationToken.None));

        Assert.Multiple(
            () => Assert.IsType<InvalidOperationException>(exception),
            () => Assert.Equal(ProcessProviderWebhookUseCase.MaxAttempts, _webhookEvents.RecordAttempts),
            () => Assert.Empty(_webhookEvents.Recorded));
    }

    [Fact]
    public async Task ExecuteAsync_UnknownPayment_ReturnsPaymentNotFoundAndRecordsNothing()
    {
        var existing = SeedPayment();
        var request = Request(_fixture.Create<Guid>(), ProviderPaymentStatus.Succeeded);

        var result = await _useCase.ExecuteAsync(request, CancellationToken.None);

        Assert.Multiple(
            () => Assert.Equal(ProviderWebhookOutcome.PaymentNotFound, result.Outcome),
            () => Assert.Empty(result.Errors),
            () => Assert.Equal(PaymentStatus.Pending, existing.Status),
            () => Assert.Equal(0, _webhookEvents.RecordAttempts));
    }

    [Fact]
    public async Task ExecuteAsync_InvalidRequest_ReturnsInvalidWithErrorsAndRecordsNothing()
    {
        var payment = SeedPayment();
        var request = Request(payment.Id, ProviderPaymentStatus.Succeeded) with { EventId = string.Empty };

        var result = await _useCase.ExecuteAsync(request, CancellationToken.None);

        Assert.Multiple(
            () => Assert.Equal(ProviderWebhookOutcome.Invalid, result.Outcome),
            () => Assert.Equal("eventId", Assert.Single(result.Errors).Key),
            () => Assert.Equal("EventId is required.", Assert.Single(result.Errors["eventId"])),
            () => Assert.Equal(PaymentStatus.Pending, payment.Status),
            () => Assert.Equal(0, _webhookEvents.RecordAttempts));
    }

    private Payment NewPayment() =>
        new(_fixture.Create<int>() + 0.99m, _fixture.Create<Currency>(), _fixture.Create<PaymentMethod>());

    private Payment SeedPayment(PaymentStatus status = PaymentStatus.Pending)
    {
        var payment = NewPayment();
        if (status != PaymentStatus.Pending)
        {
            Assert.True(payment.TryChangeStatus(status));
        }

        _payments.Added.Add(payment);
        return payment;
    }

    private ProcessedWebhookEvent StoredEventForOtherPayment(ProviderWebhookRequest request)
    {
        var otherPayment = SeedPayment();
        return new ProcessedWebhookEvent(
            request.EventId,
            otherPayment.Id,
            WebhookPayloadHash.Compute(otherPayment.Id, request.Status));
    }

    private ProviderWebhookRequest Request(Guid paymentId, ProviderPaymentStatus status) => new()
    {
        EventId = $"evt_{_fixture.Create<Guid>():N}",
        PaymentId = paymentId,
        Status = status,
    };
}
