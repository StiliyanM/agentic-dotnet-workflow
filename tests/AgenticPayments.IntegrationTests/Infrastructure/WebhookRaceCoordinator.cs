using AgenticPayments.Application.Webhooks;

namespace AgenticPayments.IntegrationTests.Infrastructure;

// Forces two webhook requests for one payment to overlap at the persistence boundary:
// both requests load the payment before either one saves, and the event firstEventId saves first.
public sealed class WebhookRaceCoordinator(string firstEventId)
{
    // Only a safety net, so that a broken implementation fails the test instead of blocking it.
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(30);

    private readonly TaskCompletionSource _bothLoaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _firstRecorded = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _loadedCount;

    // Called after a request loaded the payment for the first time. Returns when both requests have loaded it.
    public async Task PaymentLoadedAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Increment(ref _loadedCount) == 2)
        {
            _bothLoaded.TrySetResult();
        }

        await _bothLoaded.Task.WaitAsync(WaitLimit, cancellationToken);
    }

    // Lets the first event save at once. The other event saves only after the first save has returned.
    public async Task<WebhookRecordResult> RecordInOrderAsync(
        string eventId,
        Func<Task<WebhookRecordResult>> record,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (eventId != firstEventId)
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
