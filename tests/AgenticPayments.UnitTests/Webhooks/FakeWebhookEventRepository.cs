using AgenticPayments.Application.Webhooks;
using AgenticPayments.Domain.Payments;
using AgenticPayments.Domain.Webhooks;

namespace AgenticPayments.UnitTests.Webhooks;

public sealed class FakeWebhookEventRepository : IWebhookEventRepository
{
    // Events that were stored before the test, for example by an earlier delivery.
    public List<ProcessedWebhookEvent> Stored { get; } = [];

    // The events that RecordAsync saved (result Recorded), with the payment that was saved with them.
    public List<(ProcessedWebhookEvent Event, Payment Payment)> Recorded { get; } = [];

    // The results of the next RecordAsync calls, in order. When it is empty, RecordAsync saves and returns Recorded.
    // DuplicateEvent simulates a concurrent delivery of the same event id; PaymentChanged simulates a concurrent status change.
    public Queue<WebhookRecordResult> RecordResults { get; } = new();

    // Runs before RecordAsync returns its result, for example to change the stored payment between attempts.
    public Action<WebhookRecordResult>? BeforeResultReturned { get; set; }

    public int RecordAttempts { get; private set; }

    public Task<ProcessedWebhookEvent?> FindAsync(string eventId, CancellationToken cancellationToken) =>
        Task.FromResult(
            Stored.SingleOrDefault(e => e.EventId == eventId)
            ?? Recorded.Select(r => r.Event).SingleOrDefault(e => e.EventId == eventId));

    public Task<WebhookRecordResult> RecordAsync(
        ProcessedWebhookEvent webhookEvent,
        Payment payment,
        CancellationToken cancellationToken)
    {
        RecordAttempts++;
        var result = RecordResults.Count > 0 ? RecordResults.Dequeue() : WebhookRecordResult.Recorded;
        if (result == WebhookRecordResult.Recorded)
        {
            Recorded.Add((webhookEvent, payment));
        }

        BeforeResultReturned?.Invoke(result);
        return Task.FromResult(result);
    }
}
