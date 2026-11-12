using Microsoft.Management.Infrastructure;
using WinParted.Core.Devices;
using WinParted.Storage.Cim;

namespace WinParted.Storage;

/// <summary>
/// Device change notifications (design D9, spike 2.1): SMP arrival/departure for whole disks plus
/// Win32_VolumeChangeEvent for volumes, because SMP sends nothing for partition changes.
/// Not debounced; combine with <see cref="ChangeDebouncer"/>.
/// </summary>
public sealed class SmpDeviceChangeSource : IDeviceChangeSource
{
    private static readonly string[] DiskClasses = ["MSFT_PhysicalDisk", "MSFT_Disk"];

    private readonly CimSession _session = CimSession.Create(null);
    private readonly List<IDisposable> _subscriptions = [];

    public event EventHandler? Changed;

    /// <summary>Subscriptions that could not be started (empty when everything works).</summary>
    public List<string> StartErrors { get; } = [];

    public void Start()
    {
        Subscribe(Smp.StorageNamespace, "SELECT * FROM MSFT_StorageArrivalEvent", IsDiskEvent);
        Subscribe(Smp.StorageNamespace, "SELECT * FROM MSFT_StorageDepartureEvent", IsDiskEvent);
        Subscribe(Smp.Cimv2, "SELECT * FROM Win32_VolumeChangeEvent", _ => true);
    }

    public void Dispose()
    {
        foreach (var s in _subscriptions)
        {
            s.Dispose();
        }

        _subscriptions.Clear();
        _session.Dispose();
    }

    // Format jobs also raise storage events; only disks matter here.
    private static bool IsDiskEvent(CimInstance e) => DiskClasses.Contains(Smp.Str(e, "SourceClassName"));

    private void Subscribe(string ns, string query, Func<CimInstance, bool> filter)
    {
        try
        {
            var observer = new Observer(e =>
            {
                if (filter(e))
                {
                    Changed?.Invoke(this, EventArgs.Empty);
                }
            });
            _subscriptions.Add(_session.SubscribeAsync(ns, "WQL", query).Subscribe(observer));
        }
        catch (CimException ex)
        {
            StartErrors.Add($"{query}: {ex.Message}");
        }
    }

    private sealed class Observer(Action<CimInstance> onEvent) : IObserver<CimSubscriptionResult>
    {
        public void OnNext(CimSubscriptionResult value) => onEvent(value.Instance);

        public void OnError(Exception error)
        {
        }

        public void OnCompleted()
        {
        }
    }
}
