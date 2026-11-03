namespace Kofre.Server.Maintenance;

/// <summary>
/// While on, writes are refused and reads still work. Turning it on waits for the writes
/// already running, so nothing changes in the data folder until it is off again.
/// </summary>
internal sealed class MaintenanceMode
{
    private readonly Lock _lock = new();
    private int _writes;
    private TaskCompletionSource? _drained;

    public bool IsOn { get; private set; }

    /// <summary>Starts a write; false while maintenance is on. Every started write is ended by <see cref="EndWrite"/>.</summary>
    public bool TryStartWrite()
    {
        lock (_lock)
        {
            if (IsOn)
            {
                return false;
            }

            _writes++;
            return true;
        }
    }

    public void EndWrite()
    {
        lock (_lock)
        {
            if (--_writes == 0)
            {
                _drained?.TrySetResult();
            }
        }
    }

    /// <summary>Turns maintenance on and waits for the running writes; false, and off again, when they outlast <paramref name="timeout"/>.</summary>
    public async Task<bool> TurnOnAsync(TimeSpan timeout, TimeProvider clock, CancellationToken cancellation)
    {
        Task drained;
        lock (_lock)
        {
            IsOn = true;
            _drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (_writes == 0)
            {
                _drained.TrySetResult();
            }

            drained = _drained.Task;
        }

        try
        {
            await drained.WaitAsync(timeout, clock, cancellation);
            return true;
        }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
        {
            TurnOff();
            return false;
        }
    }

    public void TurnOff()
    {
        lock (_lock)
        {
            IsOn = false;
            _drained = null;
        }
    }
}
