using System.Collections.Immutable;
using WinParted.Core.Model;

namespace WinParted.Core.Devices;

/// <summary>What a media check compares: the values that change when media goes in or out.</summary>
public sealed record DiskMediaState(int Number, ulong Size, bool IsOffline, bool HasNoMedia)
{
    public static DiskMediaState Of(Disk disk) => new(disk.Number, disk.Size, disk.IsOffline, disk.HasNoMedia);
}

/// <summary>
/// Finds media inserted into or removed from a connected drive, such as a card reader slot, for
/// which Windows sends no event (change empty-card-reader-slots, D8). Every <see cref="Interval"/>
/// it reads the watched disks and raises <see cref="Changed"/> when their values differ from the
/// last applied snapshot (or the last check after it). Only disks without media and removable media disks are watched; with none, nothing
/// is read. Not debounced; combine with <see cref="ChangeDebouncer"/>.
/// </summary>
public sealed class MediaPoller : IDeviceChangeSource
{
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(2);

    private readonly Func<ImmutableArray<int>, IReadOnlyList<DiskMediaState>> _read;
    private readonly ITimer _timer;
    private readonly object _gate = new();
    private ImmutableArray<int> _watched = [];
    private HashSet<DiskId> _watchedIds = [];
    private IReadOnlyList<DiskMediaState> _last = [];
    private int _checking;
    private volatile bool _paused;
    private bool _disposed;

    /// <param name="read">Reads the current values of the given disk numbers. May be slow or throw.</param>
    public MediaPoller(Func<ImmutableArray<int>, IReadOnlyList<DiskMediaState>> read, TimeProvider? timeProvider = null, TimeSpan? interval = null)
    {
        _read = read;
        Interval = interval ?? DefaultInterval;
        _timer = (timeProvider ?? TimeProvider.System).CreateTimer(_ => Check(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public event EventHandler? Changed;

    public TimeSpan Interval { get; }

    /// <summary>
    /// No checks while true (window minimized, operation running). The values of the last snapshot
    /// are kept, so a change made meanwhile is found by the first check afterwards.
    /// </summary>
    public bool IsPaused
    {
        get => _paused;
        set => _paused = value;
    }

    /// <summary>The disk numbers currently watched.</summary>
    public ImmutableArray<int> Watched
    {
        get
        {
            lock (_gate)
            {
                return _watched;
            }
        }
    }

    /// <summary>Disks whose media can change without an event: no media now, or removable media.</summary>
    public static ImmutableArray<int> DisksToWatch(StorageSnapshot snapshot) =>
        [.. snapshot.Disks.Where(d => d.HasNoMedia || d.IsRemovableMedia).Select(d => d.Number).Order()];

    /// <summary>
    /// Takes the disks to watch from a freshly applied snapshot. A disk watched once stays watched
    /// while it's connected: right after a card is inserted, Windows can report the slot as neither
    /// empty nor removable for a moment, and the slot must still be watched for the card's removal.
    /// </summary>
    public void Watch(StorageSnapshot snapshot)
    {
        lock (_gate)
        {
            var disks = snapshot.Disks
                .Where(d => d.HasNoMedia || d.IsRemovableMedia || _watchedIds.Contains(d.Id))
                .OrderBy(d => d.Number)
                .ToList();
            _watchedIds = [.. disks.Select(d => d.Id)];
            _watched = [.. disks.Select(d => d.Number)];
            _last = [.. disks.Select(DiskMediaState.Of)];
        }
    }

    public void Start() => _timer.Change(Interval, Interval);

    /// <summary>One check. Skipped while paused, with nothing to watch, or while the previous check still runs.</summary>
    public void Check()
    {
        if (_paused)
        {
            return;
        }

        ImmutableArray<int> watched;
        lock (_gate)
        {
            if (_disposed || _watched.IsEmpty)
            {
                return;
            }

            watched = _watched;
        }

        if (Interlocked.Exchange(ref _checking, 1) == 1)
        {
            return;
        }

        try
        {
            IReadOnlyList<DiskMediaState> now;
            try
            {
                now = _read(watched);
            }
            catch (Exception)
            {
                // A failed check (device gone, WMI busy) is ignored; the next one tries again.
                return;
            }

            bool changed;
            lock (_gate)
            {
                if (_disposed || !watched.SequenceEqual(_watched))
                {
                    return;
                }

                changed = !_last.SequenceEqual(now);
                _last = now;
            }

            if (changed)
            {
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _checking, 0);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _timer.Dispose();
        }
    }
}
