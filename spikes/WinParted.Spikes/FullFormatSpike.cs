using Microsoft.Management.Infrastructure;

namespace WinParted.Spikes;

/// <summary>
/// Task 2.4: full (non-quick) format returned 1 "not supported" on an expandable VHDX. Repeat on
/// a fixed-size VHDX (behaves like a real disk: no thin provisioning) and on an expandable one for
/// comparison. Also checks whether a drive letter Windows assigns on its own can be removed.
/// </summary>
internal static class FullFormatSpike
{
    public static void Run(Smp smp)
    {
        SpikeVhd.Section("fixed", () => FullFormat(smp, fixedSize: true));
        SpikeVhd.Section("expandable", () => FullFormat(smp, fixedSize: false));
    }

    private static void FullFormat(Smp smp, bool fixedSize)
    {
        var kind = fixedSize ? "fixed" : "expandable";
        Log.Section($"Full format on a 1 GB {kind} VHDX");
        using var vhd = SpikeVhd.Create(smp, $"fullformat-{kind}", 1024, fixedSize: fixedSize);
        BehaviorSpike.BringUsable(smp, vhd);
        smp.Call(vhd.Disk(), "Initialize", ("PartitionStyle", SmpConst.StyleGpt, CimType.UInt16));
        smp.Call(vhd.Disk(), "CreatePartition", ("UseMaximumSize", true, CimType.Boolean), ("AssignDriveLetter", false, CimType.Boolean));

        Log.Line("-- Drive letter check: 3 s after CreatePartition(AssignDriveLetter=false)");
        Thread.Sleep(3000);
        vhd.LogLayout("after create");
        RemoveLetters(smp, vhd);

        Log.Line("-- Quick format NTFS (baseline)");
        smp.Call(Volume(vhd)!, "Format", ("FileSystem", "NTFS", CimType.String), ("Full", false, CimType.Boolean));

        Log.Line("-- Full format NTFS, sync");
        smp.Call(Volume(vhd)!, "Format", ("FileSystem", "NTFS", CimType.String), ("Full", true, CimType.Boolean));

        Log.Line("-- Full format NTFS, async with progress callbacks");
        smp.CallWithProgress(Volume(vhd)!, "Format", ("FileSystem", "NTFS", CimType.String), ("Full", true, CimType.Boolean));

        Log.Line("-- Full format exFAT, async with progress callbacks");
        smp.CallWithProgress(Volume(vhd)!, "Format", ("FileSystem", "exFAT", CimType.String), ("Full", true, CimType.Boolean));

        Log.Line("-- Full format NTFS with Force=true, sync");
        smp.Call(Volume(vhd)!, "Format", ("FileSystem", "NTFS", CimType.String), ("Full", true, CimType.Boolean), ("Force", true, CimType.Boolean));

        vhd.LogLayout("end");
        RemoveLetters(smp, vhd);
    }

    private static CimInstance? Volume(SpikeVhd vhd) =>
        vhd.Partitions().LastOrDefault() is { } p ? vhd.VolumeOf(p) : null;

    private static void RemoveLetters(Smp smp, SpikeVhd vhd)
    {
        foreach (var p in vhd.Partitions())
        {
            if (Smp.Prop(p, "DriveLetter") is char c && c != '\0')
            {
                Log.Line($"Windows assigned {c}: on its own; removing it.");
                smp.Call(p, "RemoveAccessPath", ("AccessPath", $"{c}:\\", CimType.String));
                Log.Line($"Letter now: {Log.Format(Smp.Prop(vhd.Partitions().First(x => Equals(Smp.Prop(x, "Offset"), Smp.Prop(p, "Offset"))), "DriveLetter"))}");
            }
        }
    }
}
