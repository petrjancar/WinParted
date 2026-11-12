using System.Diagnostics;
using Microsoft.Management.Infrastructure;

namespace WinParted.Spikes;

/// <summary>
/// Task 2.2: does SMP report progress for Format and Resize, and how? Tries two mechanisms on a
/// throwaway 4 GB VHDX: (A) async call with streaming + WriteProgress callbacks, and
/// (B) RunAsJob=true and polling MSFT_StorageJob.
/// </summary>
internal static class ProgressSpike
{
    private const ulong MB = 1024 * 1024;

    public static void Run(Smp smp)
    {
        Log.Section("Progress: setup");
        using var vhd = SpikeVhd.Create(smp, "progress", 4096);
        BehaviorSpike.BringUsable(smp, vhd);
        smp.Call(vhd.Disk(), "Initialize", ("PartitionStyle", SmpConst.StyleGpt, CimType.UInt16));
        smp.Call(vhd.Disk(), "CreatePartition", ("UseMaximumSize", true, CimType.Boolean), ("AssignDriveLetter", false, CimType.Boolean));
        var vol = Volume(vhd);
        if (vol is null)
        {
            Log.Line("No volume object; aborting.");
            return;
        }

        smp.Call(vol, "Format", ("FileSystem", "NTFS", CimType.String), ("Full", false, CimType.Boolean));

        SpikeVhd.Section("A1", () =>
        {
            Log.Section("A1: full format, async with progress callbacks");
            smp.CallWithProgress(Volume(vhd)!, "Format", ("FileSystem", "NTFS", CimType.String), ("Full", true, CimType.Boolean));
        });

        SpikeVhd.Section("B1", () =>
        {
            Log.Section("B1: full format with RunAsJob=true, polling MSFT_StorageJob");
            var job = smp.Call(Volume(vhd)!, "Format", ("FileSystem", "NTFS", CimType.String), ("Full", true, CimType.Boolean), ("RunAsJob", true, CimType.Boolean));
            PollJob(smp, job);
        });
        CimMethodResult? r;

        Log.Section("Fill the volume with 1.5 GB of data so a shrink has something to move");
        Fill(Volume(vhd)!, 1536);

        var part = vhd.Partitions().Last();
        var sizes = smp.Call(part, "GetSupportedSize");
        var min = Convert.ToUInt64(sizes?.OutParameters["SizeMin"]?.Value ?? 0UL);
        var current = Convert.ToUInt64(Smp.Prop(part, "Size"));
        var target = Math.Max(min + (64 * MB), current / 2);

        Log.Section($"A2: shrink {current} -> {target}, async with progress callbacks");
        smp.CallWithProgress(vhd.Partitions().Last(), "Resize", ("Size", target, CimType.UInt64));
        vhd.LogLayout("after shrink");

        Log.Section("B2: extend back to max with RunAsJob=true, polling MSFT_StorageJob");
        sizes = smp.Call(vhd.Partitions().Last(), "GetSupportedSize");
        var max = Convert.ToUInt64(sizes?.OutParameters["SizeMax"]?.Value ?? 0UL);
        r = smp.Call(vhd.Partitions().Last(), "Resize", ("Size", max, CimType.UInt64), ("RunAsJob", true, CimType.Boolean));
        PollJob(smp, r);
        vhd.LogLayout("after extend");

        Log.Section("A3: shrink again (sync call) for timing comparison");
        var sw = Stopwatch.StartNew();
        smp.Call(vhd.Partitions().Last(), "Resize", ("Size", target, CimType.UInt64));
        Log.Line($"sync shrink took {sw.ElapsedMilliseconds} ms");
    }

    private static CimInstance? Volume(SpikeVhd vhd) => vhd.Partitions().LastOrDefault() is { } p ? vhd.VolumeOf(p) : null;

    private static void Fill(CimInstance volume, int megabytes)
    {
        var path = (string?)Smp.Prop(volume, "Path");
        if (path is null)
        {
            Log.Line("Volume has no path; skipping fill.");
            return;
        }

        var sw = Stopwatch.StartNew();
        var buffer = new byte[4 * 1024 * 1024];
        new Random(1).NextBytes(buffer);
        for (var i = 0; i < megabytes / 64; i++)
        {
            using var f = new FileStream(Path.Combine(path, $"fill-{i:D3}.bin"), FileMode.Create);
            for (var j = 0; j < 16; j++)
            {
                f.Write(buffer);
            }
        }

        Log.Line($"Wrote {megabytes} MB in {sw.ElapsedMilliseconds} ms");
    }

    private static void PollJob(Smp smp, CimMethodResult? result)
    {
        if (result?.OutParameters["CreatedStorageJob"]?.Value is not CimInstance job)
        {
            Log.Line("No CreatedStorageJob returned; RunAsJob is probably not supported for this method.");
            return;
        }

        var jobId = (string?)Smp.Prop(job, "ObjectId") ?? (string?)Smp.Prop(job, "InstanceId");
        Log.Line($"Job created: {Log.Format(Smp.Prop(job, "Name"))} id={jobId}");
        var sw = Stopwatch.StartNew();
        string? last = null;
        while (sw.Elapsed < TimeSpan.FromMinutes(10))
        {
            var current = smp.Query("SELECT * FROM MSFT_StorageJob")
                .FirstOrDefault(j => (string?)Smp.Prop(j, "ObjectId") == jobId || (string?)Smp.Prop(j, "InstanceId") == jobId);
            if (current is null)
            {
                Log.Line($"Job no longer listed after {sw.ElapsedMilliseconds} ms.");
                return;
            }

            var state = $"JobState={Smp.Prop(current, "JobState")} PercentComplete={Smp.Prop(current, "PercentComplete")} ErrorCode={Smp.Prop(current, "ErrorCode")} ErrorDescription={Smp.Prop(current, "ErrorDescription")}";
            if (state != last)
            {
                Log.Line($"    JOB {state} (t={sw.ElapsedMilliseconds} ms)");
                last = state;
            }

            var jobState = Convert.ToUInt16(Smp.Prop(current, "JobState") ?? 0);
            if (jobState is 7 or 8 or 9 or 10 or 12)
            {
                return;
            }

            Thread.Sleep(250);
        }
    }
}
