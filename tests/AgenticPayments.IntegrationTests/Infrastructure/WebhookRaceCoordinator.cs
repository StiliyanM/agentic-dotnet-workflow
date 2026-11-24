using AgenticPayments.Application.Webhooks;
using AgenticPayments.Domain.Webhooks;

namespace AgenticPayments.IntegrationTests.Infrastructure;

// Forces two webhook requests to overlap at the persistence boundary:
// both requests load the payment before either one saves, and the first event that matches isFirst saves first.
// A predicate (not only the event id) selects the first request, so that two requests with the same event id
// and different payloads can be ordered.
public sealed class WebhookRaceCoordinator(Func<ProcessedWebhookEvent, bool> isFirst)
{
    // Only a safety net, so that a broken implementation fails the test instead of blocking it.
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(30);

    private readonly TaskCompletionSource _bothLoaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _firstRecorded = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _loadedCount;
    private int _firstClaimed;

    // Called after a request loaded the payment for the first time. Returns when both requests have loaded it.
    public async Task PaymentLoadedAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Increment(ref _loadedCount) == 2)
        {
            _bothLoaded.TrySetResult();
        }

        await _bothLoaded.Task.WaitAsync(WaitLimit, cancellationToken);
    }

    // Lets the first matching event save at once. Every other save waits until that save has returned.
    // The claim is atomic, so that only one request saves first when both events match (equal payloads).
    public async Task<WebhookRecordResult> RecordInOrderAsync(
        ProcessedWebhookEvent webhookEvent,
        Func<Task<WebhookRecordResult>> record,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(webhookEvent);
        ArgumentNullException.ThrowIfNull(record);

        if (!isFirst(webhookEvent) || Interlocked.CompareExchange(ref _firstClaimed, 1, 0) != 0)
        {
            await _firstRecorded.Task.WaitAsync(WaitLimit, cancellationToken);
            return await record();
        }

        try
        {
            return await record();
        }
        finally
        {
            _firstRecorded.TrySetResult();
        }
    }
}
