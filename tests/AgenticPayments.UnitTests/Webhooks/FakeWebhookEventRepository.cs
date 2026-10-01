using AgenticPayments.Application.Webhooks;
using AgenticPayments.Domain.Payments;
using AgenticPayments.Domain.Webhooks;

namespace AgenticPayments.UnitTests.Webhooks;

public sealed class FakeWebhookEventRepository : IWebhookEventRepository
{
    // Event ids that were stored before the test, for example by an earlier delivery.
    public HashSet<string> StoredEventIds { get; } = new(StringComparer.Ordinal);

    public List<(ProcessedWebhookEvent Event, Payment Payment)> Recorded { get; } = [];

    // Simulates a concurrent delivery that stored the same event id after ExistsAsync returned false.
    public bool RejectRecord { get; set; }

    public int RecordAttempts { get; private set; }

    public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken) =>
        Task.FromResult(StoredEventIds.Contains(eventId) || Recorded.Any(r => r.Event.EventId == eventId));

    public Task<bool> TryRecordAsync(ProcessedWebhookEvent webhookEvent, Payment payment, CancellationToken cancellationToken)
    {
        RecordAttempts++;
        if (RejectRecord)
        {
            return Task.FromResult(false);
        }

        Recorded.Add((webhookEvent, payment));
        return Task.FromResult(true);
    }
}
