namespace WinParted.Core.Devices;

/// <summary>
/// Raises <see cref="Changed"/> whenever disks or volumes may have changed (design D9). Raw, not
/// debounced: one physical event can produce several notifications.
/// </summary>
public interface IDeviceChangeSource : IDisposable
{
    event EventHandler? Changed;

    void Start();
}

/// <summary>
/// Collapses a burst of change notifications into one refresh (spec device-change-detection,
/// "Burst coalescing"): <see cref="Settled"/> fires once, <see cref="Delay"/> after the last signal.
/// </summary>
public sealed class ChangeDebouncer : IDisposable
{
    public static readonly TimeSpan DefaultDelay = TimeSpan.FromMilliseconds(500);

    private readonly ITimer _timer;
    private readonly object _gate = new();
    private bool _disposed;

    public ChangeDebouncer(TimeProvider? timeProvider = null, TimeSpan? delay = null)
    {
        Delay = delay ?? DefaultDelay;
        _timer = (timeProvider ?? TimeProvider.System).CreateTimer(_ => Fire(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public event EventHandler? Settled;

    public TimeSpan Delay { get; }

    /// <summary>Records a change; restarts the waiting period.</summary>
    public void Signal()
    {
        lock (_gate)
        {
            if (!_disposed)
            {
                _timer.Change(Delay, Timeout.InfiniteTimeSpan);
            }
        }
    }

    /// <summary>Connects a source so each of its notifications is a <see cref="Signal"/>.</summary>
    public void Attach(IDeviceChangeSource source) => source.Changed += (_, _) => Signal();

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _timer.Dispose();
        }
    }

    private void Fire()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
        }

        Settled?.Invoke(this, EventArgs.Empty);
    }
}
