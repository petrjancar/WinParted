using Microsoft.Management.Infrastructure;

namespace WinParted.Spikes;

/// <summary>
/// Task 2.1: which change notifications arrive, and how fast, when disks appear, change and
/// disappear. Compares SMP storage indications with Win32_DiskDrive / Win32_VolumeChangeEvent.
/// </summary>
internal static class EventsSpike
{
    private static DateTime _mark = DateTime.Now;
    private static string _markLabel = "start";

    public static void Run(Smp smp)
    {
        Log.Section("Subscribing");
        var subscriptions = new List<IDisposable>();
        Subscribe(smp, subscriptions, Smp.Ns, "SELECT * FROM MSFT_StorageArrivalEvent", "SMP Arrival");
        Subscribe(smp, subscriptions, Smp.Ns, "SELECT * FROM MSFT_StorageDepartureEvent", "SMP Departure");
        Subscribe(smp, subscriptions, Smp.Ns, "SELECT * FROM MSFT_StorageModificationEvent", "SMP Modification");
        Subscribe(smp, subscriptions, @"root\cimv2", "SELECT * FROM __InstanceCreationEvent WITHIN 1 WHERE TargetInstance ISA 'Win32_DiskDrive'", "Win32_DiskDrive created");
        Subscribe(smp, subscriptions, @"root\cimv2", "SELECT * FROM __InstanceDeletionEvent WITHIN 1 WHERE TargetInstance ISA 'Win32_DiskDrive'", "Win32_DiskDrive deleted");
        Subscribe(smp, subscriptions, @"root\cimv2", "SELECT * FROM Win32_VolumeChangeEvent", "Win32_VolumeChangeEvent");
        Thread.Sleep(2000);

        try
        {
            Log.Section("1. Attach a VHDX");
            using (var vhd = SpikeVhd.Create(smp, "events", 512, () => Mark("attach VHDX")))
            {
                Thread.Sleep(8000);

                Log.Section("2. Initialize + create partition + format (modifications)");
                BehaviorSpike.BringUsable(smp, vhd);
                Mark("initialize");
                smp.Call(vhd.Disk(), "Initialize", ("PartitionStyle", SmpConst.StyleGpt, CimType.UInt16));
                Thread.Sleep(5000);
                Mark("create partition");
                smp.Call(vhd.Disk(), "CreatePartition", ("UseMaximumSize", true, CimType.Boolean), ("AssignDriveLetter", false, CimType.Boolean));
                Thread.Sleep(3000);
                if (vhd.Partitions().LastOrDefault() is { } part && vhd.VolumeOf(part) is { } vol)
                {
                    Mark("format");
                    smp.Call(vol, "Format", ("FileSystem", "NTFS", CimType.String), ("Full", false, CimType.Boolean));
                    Thread.Sleep(3000);
                }

                Log.Section("3. Detach the VHDX");
                Mark("detach VHDX");
            }

            Thread.Sleep(8000);

            Log.Section("4. USB disk (optional)");
            UsbStep("PLUG IN a USB stick or external disk now, then press Enter (or type s + Enter to skip): ", "USB plugged in");
            UsbStep("UNPLUG it now (safely or not, nothing is written to it), then press Enter (or s + Enter to skip): ", "USB unplugged");
        }
        finally
        {
            foreach (var s in subscriptions)
            {
                s.Dispose();
            }
        }
    }

    private static void UsbStep(string prompt, string label)
    {
        Console.Write(prompt);
        var answer = Console.ReadLine();
        if (string.Equals(answer?.Trim(), "s", StringComparison.OrdinalIgnoreCase))
        {
            Log.Line($"Skipped: {label}");
            return;
        }

        Mark(label);
        Log.Line("Collecting events for 10 seconds...");
        Thread.Sleep(10000);
    }

    private static void Mark(string label)
    {
        _mark = DateTime.Now;
        _markLabel = label;
        Log.Line($">>> ACTION: {label}");
    }

    private static void Subscribe(Smp smp, List<IDisposable> subs, string ns, string wql, string label)
    {
        try
        {
            var results = smp.Session.SubscribeAsync(ns, "WQL", wql);
            subs.Add(results.Subscribe(new Observer(label)));
            Log.Line($"Subscribed: {label}");
        }
        catch (CimException ex)
        {
            Smp.LogCimException($"Subscribe {label}", ex);
        }
    }

    private sealed class Observer(string label) : IObserver<CimSubscriptionResult>
    {
        public void OnNext(CimSubscriptionResult value)
        {
            var i = value.Instance;
            var latency = (DateTime.Now - _mark).TotalMilliseconds;
            var detail = i.CimSystemProperties.ClassName switch
            {
                "MSFT_StorageArrivalEvent" or "MSFT_StorageDepartureEvent" or "MSFT_StorageModificationEvent" =>
                    $"SourceClassName={Smp.Prop(i, "SourceClassName")} SourceObjectId={Smp.Prop(i, "SourceObjectId")}",
                "Win32_VolumeChangeEvent" => $"DriveName={Smp.Prop(i, "DriveName")} EventType={Smp.Prop(i, "EventType")}",
                _ => Smp.Prop(i, "TargetInstance") is CimInstance t ? $"Model={Smp.Prop(t, "Model")} Index={Smp.Prop(t, "Index")}" : "",
            };
            Log.Line($"EVENT [{label}] +{latency:F0} ms after '{_markLabel}': {detail}");
        }

        public void OnError(Exception error) => Log.Line($"EVENT [{label}] subscription error: {error.Message}");

        public void OnCompleted() => Log.Line($"EVENT [{label}] subscription completed");
    }
}
