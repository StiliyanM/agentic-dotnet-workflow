using AgenticPayments.Application.Webhooks;
using AgenticPayments.Domain.Payments;
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
    public async Task ExecuteAsync_NewEvent_ChangesPaymentStatusAndRecordsEvent(
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
            () => Assert.Same(payment, recorded.Payment));
    }

    [Fact]
    public async Task ExecuteAsync_KnownEventId_ReturnsDuplicateAndChangesNothing()
    {
        var payment = SeedPayment();
        var request = Request(payment.Id, ProviderPaymentStatus.Succeeded);
        _webhookEvents.StoredEventIds.Add(request.EventId);

        var result = await _useCase.ExecuteAsync(request, CancellationToken.None);

        Assert.Multiple(
            () => Assert.Equal(ProviderWebhookOutcome.Duplicate, result.Outcome),
            () => Assert.Empty(result.Errors),
            () => Assert.Equal(PaymentStatus.Pending, payment.Status),
            () => Assert.Empty(_webhookEvents.Recorded));
    }

    [Fact]
    public async Task ExecuteAsync_RecordRejected_ReturnsDuplicate()
    {
        var payment = SeedPayment();
        var request = Request(payment.Id, ProviderPaymentStatus.Failed);
        _webhookEvents.RejectRecord = true;

        var result = await _useCase.ExecuteAsync(request, CancellationToken.None);

        Assert.Multiple(
            () => Assert.Equal(ProviderWebhookOutcome.Duplicate, result.Outcome),
            () => Assert.Empty(result.Errors),
            () => Assert.Equal(1, _webhookEvents.RecordAttempts));
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
            () => Assert.Empty(_webhookEvents.Recorded));
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
            () => Assert.Empty(_webhookEvents.Recorded));
    }

    private Payment SeedPayment()
    {
        var payment = new Payment(_fixture.Create<int>() + 0.99m, _fixture.Create<Currency>(), _fixture.Create<PaymentMethod>());
        _payments.Added.Add(payment);
        return payment;
    }

    private ProviderWebhookRequest Request(Guid paymentId, ProviderPaymentStatus status) => new()
    {
        EventId = $"evt_{_fixture.Create<Guid>():N}",
        PaymentId = paymentId,
        Status = status,
    };
}
