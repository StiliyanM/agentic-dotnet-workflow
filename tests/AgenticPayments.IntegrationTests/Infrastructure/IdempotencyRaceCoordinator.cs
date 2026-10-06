using AgenticPayments.Application.Payments;

namespace AgenticPayments.IntegrationTests.Infrastructure;

// Forces two create requests with the same Idempotency-Key to overlap at the persistence boundary:
// both requests look up the key before either one saves, and the second save starts only after the first has returned.
public sealed class IdempotencyRaceCoordinator
{
    // Only a safety net, so that a broken implementation fails the test instead of blocking it.
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(30);

    private readonly TaskCompletionSource _bothLookedUp = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _firstSaved = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _lookupCount;
    private int _saveCount;

    public int LookupCount => Volatile.Read(ref _lookupCount);

    // Called after a request looked up the key for the first time. Returns when both requests have looked it up.
    public async Task KeyLookedUpAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Increment(ref _lookupCount) == 2)
        {
            _bothLookedUp.TrySetResult();
        }

        await _bothLookedUp.Task.WaitAsync(WaitLimit, cancellationToken);
    }

    // Lets the first save run at once. The other save runs only after the first save has returned.
    public async Task<IdempotencySaveResult> SaveInOrderAsync(
        Func<Task<IdempotencySaveResult>> save,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(save);

        if (Interlocked.Increment(ref _saveCount) != 1)
        {
            await _firstSaved.Task.WaitAsync(WaitLimit, cancellationToken);
            return await save();
        }

        try
        {
            return await save();
        }
        finally
        {
            _firstSaved.TrySetResult();
        }
    }
}
