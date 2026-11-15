using Microsoft.Management.Infrastructure;

namespace WinParted.Spikes;

/// <summary>
/// turn-off-disk-read-only, tasks 1.1 and 1.2: how <c>MSFT_Disk.SetAttributes(IsReadOnly=false)</c>
/// behaves. <see cref="Run"/> works only on throwaway VHDX files. <see cref="RunCard"/> touches one
/// removable disk the user confirms, changes only its read-only attribute and restores it.
/// </summary>
internal static class ReadOnlySpike
{
    private const ushort BusUsb = 7;
    private const ushort BusSd = 12;
    private const ushort BusMmc = 13;

    public static void Run(Smp smp)
    {
        SpikeVhd.Section("Online disk", () => OnlineDisk(smp));
        SpikeVhd.Section("Offline disk", () => OfflineDisk(smp));
        SpikeVhd.Section("Attached read-only", () => AttachedReadOnly(smp));
    }

    private static void OnlineDisk(Smp smp)
    {
        Log.Section("Online: set read-only, then clear it");
        using var vhd = SpikeVhd.Create(smp, "readonly-online", 256);
        BehaviorSpike.BringUsable(smp, vhd);
        smp.Call(vhd.Disk(), "Initialize", ("PartitionStyle", SmpConst.StyleGpt, CimType.UInt16));
        smp.Call(vhd.Disk(), "SetAttributes", ("IsReadOnly", true, CimType.Boolean));
        State(vhd, "after SetAttributes(IsReadOnly=true)");

        Log.Line("-- A write on the read-only disk (expect the read-only code)");
        smp.Call(vhd.Disk(), "CreatePartition", ("Size", 32UL * 1024 * 1024, CimType.UInt64), ("AssignDriveLetter", false, CimType.Boolean));

        smp.Call(vhd.Disk(), "SetAttributes", ("IsReadOnly", false, CimType.Boolean));
        State(vhd, "after SetAttributes(IsReadOnly=false)");

        Log.Line("-- Clearing again on a writable disk (no-op?)");
        smp.Call(vhd.Disk(), "SetAttributes", ("IsReadOnly", false, CimType.Boolean));
        State(vhd, "after a second SetAttributes(IsReadOnly=false)");
    }

    private static void OfflineDisk(Smp smp)
    {
        Log.Section("Offline: clear read-only while the disk is offline");
        using var vhd = SpikeVhd.Create(smp, "readonly-offline", 256);
        BehaviorSpike.BringUsable(smp, vhd);
        smp.Call(vhd.Disk(), "SetAttributes", ("IsReadOnly", true, CimType.Boolean));
        smp.Call(vhd.Disk(), "Offline");
        State(vhd, "read-only and offline");

        smp.Call(vhd.Disk(), "SetAttributes", ("IsReadOnly", false, CimType.Boolean));
        State(vhd, "after SetAttributes(IsReadOnly=false) while offline");

        smp.Call(vhd.Disk(), "Online");
        State(vhd, "after Online");
    }

    private static void AttachedReadOnly(Smp smp)
    {
        Log.Section("Disk image attached read-only: try to clear the flag");
        using var vhd = SpikeVhd.Create(smp, "readonly-attached", 256, attachReadOnly: true);
        State(vhd, "right after a read-only attach");
        if (Smp.Prop(vhd.Disk(), "IsOffline") as bool? == true)
        {
            smp.Call(vhd.Disk(), "Online");
            State(vhd, "after Online");
        }

        smp.Call(vhd.Disk(), "SetAttributes", ("IsReadOnly", false, CimType.Boolean));
        State(vhd, "after SetAttributes(IsReadOnly=false)");
    }

    private static void State(SpikeVhd vhd, string title)
    {
        var d = vhd.Disk();
        Log.Line($"--- {title}: IsReadOnly={Smp.Prop(d, "IsReadOnly")} IsOffline={Smp.Prop(d, "IsOffline")} OfflineReason={Smp.Prop(d, "OfflineReason")} OperationalStatus={Log.Format(Smp.Prop(d, "OperationalStatus"))} HealthStatus={Smp.Prop(d, "HealthStatus")}");
    }

    /// <summary>
    /// Task 1.2 (optional): an SD card or USB stick with its lock switch ON. Only the read-only
    /// attribute is touched, and it is set back if the call changed it. No data is written.
    /// </summary>
    public static void RunCard(Smp smp)
    {
        Log.Section("Locked card or stick: try to clear the read-only flag");
        var candidates = smp.Query("SELECT * FROM MSFT_Disk")
            .Where(d => Convert.ToUInt16(Smp.Prop(d, "BusType")) is BusUsb or BusSd or BusMmc
                && Smp.Prop(d, "IsReadOnly") as bool? == true
                && Smp.Prop(d, "IsBoot") as bool? != true
                && Smp.Prop(d, "IsSystem") as bool? != true)
            .ToList();
        if (candidates.Count != 1)
        {
            Log.Line($"Found {candidates.Count} read-only USB/SD disks; need exactly one. Insert the card with its lock switch ON and remove other read-only removable disks. Nothing was changed.");
            return;
        }

        var disk = candidates[0];
        var number = Convert.ToUInt32(Smp.Prop(disk, "Number"));
        Log.Instance(disk, "Candidate");
        Console.Write($"\nDisk {number} ({Smp.Prop(disk, "FriendlyName")}, {Convert.ToUInt64(Smp.Prop(disk, "Size")) / (1024 * 1024)} MB) will have ONLY its read-only attribute cleared, then restored. Type {number} to continue: ");
        if (Console.ReadLine()?.Trim() != number.ToString(System.Globalization.CultureInfo.InvariantCulture))
        {
            Log.Line("Not confirmed. Nothing was changed.");
            return;
        }

        smp.Call(disk, "SetAttributes", ("IsReadOnly", false, CimType.Boolean));
        var after = smp.Query("SELECT * FROM MSFT_Disk").First(d => Convert.ToUInt32(Smp.Prop(d, "Number")) == number);
        var stillReadOnly = Smp.Prop(after, "IsReadOnly") as bool? == true;
        Log.Line($"--- after SetAttributes(IsReadOnly=false): IsReadOnly={stillReadOnly}");
        if (!stillReadOnly)
        {
            Log.Line("The flag changed (no hardware lock?). Restoring it.");
            smp.Call(after, "SetAttributes", ("IsReadOnly", true, CimType.Boolean));
        }
    }
}
