using Microsoft.Management.Infrastructure;

namespace WinParted.Spikes;

/// <summary>
/// Task 2.3: how SMP behaves on MBR limits, and which status codes it returns for common
/// failures. Works only on throwaway VHDX files.
/// </summary>
internal static class BehaviorSpike
{
    private const ulong MB = 1024 * 1024;

    public static void Run(Smp smp)
    {
        SpikeVhd.Section("MBR limit", () => MbrLimit(smp));
        SpikeVhd.Section("Failure codes", () => Failures(smp));
        SpikeVhd.Section("FAT32 limit", () => Fat32Limit(smp));
    }

    private static void MbrLimit(Smp smp)
    {
        Log.Section("MBR: create 100 MB partitions until SMP refuses");
        using var vhd = SpikeVhd.Create(smp, "behavior-mbr", 1024);
        BringUsable(smp, vhd);

        smp.Call(vhd.Disk(), "Initialize", ("PartitionStyle", SmpConst.StyleMbr, CimType.UInt16));
        for (var i = 1; i <= 6; i++)
        {
            Log.Line($"-- partition {i}");
            var r = smp.Call(vhd.Disk(), "CreatePartition",
                ("Size", 100 * MB, CimType.UInt64),
                ("AssignDriveLetter", false, CimType.Boolean));
            if (Smp.ReturnValue(r) is not 0)
            {
                Log.Line($"SMP refused partition {i}.");
            }

            vhd.LogLayout($"after attempt {i}");
        }

        Log.Section("MBR: Initialize an already initialized disk");
        smp.Call(vhd.Disk(), "Initialize", ("PartitionStyle", SmpConst.StyleGpt, CimType.UInt16));

        Log.Section("Clear disk");
        smp.Call(vhd.Disk(), "Clear",
            ("RemoveData", true, CimType.Boolean),
            ("RemoveOEM", true, CimType.Boolean),
            ("ZeroOutEntireDisk", false, CimType.Boolean));
        vhd.LogLayout("after Clear");
    }

    private static void Failures(Smp smp)
    {
        Log.Section("Failure codes on a GPT disk");
        using var vhd = SpikeVhd.Create(smp, "behavior-gpt", 1024);
        BringUsable(smp, vhd);
        smp.Call(vhd.Disk(), "Initialize", ("PartitionStyle", SmpConst.StyleGpt, CimType.UInt16));
        vhd.LogLayout("after Initialize GPT (shows where Windows puts the first partition)");

        Log.Line("-- CreatePartition larger than the disk");
        smp.Call(vhd.Disk(), "CreatePartition", ("Size", 5000 * MB, CimType.UInt64), ("AssignDriveLetter", false, CimType.Boolean));

        Log.Line("-- CreatePartition 300 MB + quick format NTFS");
        smp.Call(vhd.Disk(), "CreatePartition", ("Size", 300 * MB, CimType.UInt64), ("AssignDriveLetter", false, CimType.Boolean));
        var part = vhd.Partitions().LastOrDefault(p => Convert.ToUInt64(Smp.Prop(p, "Size")) >= 200 * MB);
        var vol = part is null ? null : vhd.VolumeOf(part);
        if (part is null || vol is null)
        {
            Log.Line("No volume object for the new partition; skipping volume tests.");
            return;
        }

        smp.Call(vol, "Format", ("FileSystem", "NTFS", CimType.String), ("FileSystemLabel", "SPIKE", CimType.String), ("Full", false, CimType.Boolean));
        vhd.LogLayout("after create + format");

        Log.Line("-- GetSupportedSize, then Resize above max and below min");
        part = vhd.Partitions().Last(p => Convert.ToUInt64(Smp.Prop(p, "Size")) >= 200 * MB);
        var sizes = smp.Call(part, "GetSupportedSize");
        var max = Convert.ToUInt64(sizes?.OutParameters["SizeMax"]?.Value ?? 0UL);
        var min = Convert.ToUInt64(sizes?.OutParameters["SizeMin"]?.Value ?? 0UL);
        smp.Call(part, "Resize", ("Size", max + (100 * MB), CimType.UInt64));
        smp.Call(vhd.Partitions().Last(), "Resize", ("Size", Math.Max(min, 2 * MB) / 2, CimType.UInt64));

        Log.Line("-- Format while a file on the volume is held open (no Force)");
        vol = vhd.VolumeOf(vhd.Partitions().Last())!;
        var volumePath = (string?)Smp.Prop(vol, "Path");
        Log.Line($"Volume path: {volumePath}");
        if (volumePath is not null)
        {
            try
            {
                using var held = new FileStream(Path.Combine(volumePath, "held.bin"), FileMode.Create, FileAccess.ReadWrite, FileShare.None);
                held.Write(new byte[4096]);
                held.Flush();
                smp.Call(vol, "Format", ("FileSystem", "NTFS", CimType.String), ("Full", false, CimType.Boolean));
                Log.Line("-- Delete partition while the file is held open");
                smp.Call(vhd.Partitions().Last(), "DeleteObject");
            }
            catch (Exception ex)
            {
                Log.Line($"Holding a file open failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        vhd.LogLayout("after in-use tests");

        Log.Line("-- SetFileSystemLabel too long for NTFS (40 chars)");
        if (vhd.Partitions().LastOrDefault() is { } lastPart && vhd.VolumeOf(lastPart) is { } labelVol)
        {
            smp.Call(labelVol, "SetFileSystemLabel", ("FileSystemLabel", new string('L', 40), CimType.String));
        }

        Log.Line("-- Delete a partition, then call Delete again on the stale instance");
        var stale = vhd.Partitions().Last();
        smp.Call(stale, "DeleteObject");
        smp.Call(stale, "DeleteObject");

        Log.Line("-- Disk read-only: SetAttributes(IsReadOnly=true), then CreatePartition, then undo");
        smp.Call(vhd.Disk(), "SetAttributes", ("IsReadOnly", true, CimType.Boolean));
        smp.Call(vhd.Disk(), "CreatePartition", ("Size", 50 * MB, CimType.UInt64), ("AssignDriveLetter", false, CimType.Boolean));

        Log.Line("-- Read-only AND offline: Offline, then Online while read-only (option B check)");
        smp.Call(vhd.Disk(), "Offline");
        Log.Instance(vhd.Disk(), "Disk while offline + read-only");
        smp.Call(vhd.Disk(), "Online");
        Log.Line($"After Online: IsOffline={Smp.Prop(vhd.Disk(), "IsOffline")} IsReadOnly={Smp.Prop(vhd.Disk(), "IsReadOnly")}");
        smp.Call(vhd.Disk(), "SetAttributes", ("IsReadOnly", false, CimType.Boolean));

        Log.Line("-- Offline disk: CreatePartition while offline, then Online");
        smp.Call(vhd.Disk(), "Offline");
        smp.Call(vhd.Disk(), "CreatePartition", ("Size", 50 * MB, CimType.UInt64), ("AssignDriveLetter", false, CimType.Boolean));
        smp.Call(vhd.Disk(), "Online");
        vhd.LogLayout("end of failure tests");
    }

    private static void Fat32Limit(Smp smp)
    {
        Log.Section("FAT32 on a 40 GB partition (sparse 64 GB VHDX, uses almost no real space)");
        using var vhd = SpikeVhd.Create(smp, "behavior-fat32", 64 * 1024);
        BringUsable(smp, vhd);
        smp.Call(vhd.Disk(), "Initialize", ("PartitionStyle", SmpConst.StyleGpt, CimType.UInt16));
        smp.Call(vhd.Disk(), "CreatePartition", ("Size", 40UL * 1024 * MB, CimType.UInt64), ("AssignDriveLetter", false, CimType.Boolean));
        var part = vhd.Partitions().Last();
        if (vhd.VolumeOf(part) is not { } vol)
        {
            Log.Line("No volume object; skipping.");
            return;
        }

        smp.Call(vol, "GetSupportedFileSystems");
        smp.Call(vol, "Format", ("FileSystem", "FAT32", CimType.String), ("Full", false, CimType.Boolean));
        smp.Call(vol, "Format", ("FileSystem", "exFAT", CimType.String), ("Full", false, CimType.Boolean));
        vol = vhd.VolumeOf(vhd.Partitions().Last());
        if (vol is not null)
        {
            smp.Call(vol, "SetFileSystemLabel", ("FileSystemLabel", "SIXTEEN_CHARS_XX", CimType.String));
        }
    }

    /// <summary>Logs the attach state (offline/read-only after attach is itself a finding) and fixes it.</summary>
    internal static void BringUsable(Smp smp, SpikeVhd vhd)
    {
        var disk = vhd.Disk();
        Log.Line($"Attach state: IsOffline={Smp.Prop(disk, "IsOffline")} OfflineReason={Smp.Prop(disk, "OfflineReason")} IsReadOnly={Smp.Prop(disk, "IsReadOnly")} PartitionStyle={Smp.Prop(disk, "PartitionStyle")}");
        if (Smp.Prop(disk, "IsOffline") as bool? == true)
        {
            smp.Call(vhd.Disk(), "Online");
        }

        if (Smp.Prop(vhd.Disk(), "IsReadOnly") as bool? == true)
        {
            smp.Call(vhd.Disk(), "SetAttributes", ("IsReadOnly", false, CimType.Boolean));
        }
    }
}
