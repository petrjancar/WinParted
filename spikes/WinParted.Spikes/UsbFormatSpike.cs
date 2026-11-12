using System.Diagnostics;
using Microsoft.Management.Infrastructure;

namespace WinParted.Spikes;

/// <summary>
/// Follow-up to task 2.4 on a real, empty USB flash disk: quick and full format for NTFS, exFAT
/// and FAT32, plus fallbacks if SMP refuses a full NTFS format. ERASES the USB stick.
///
/// Safety layers (all must pass, re-checked before every change):
///  1. Bus type must be USB (7). The PC's system disk is NVMe/SATA.
///  2. Neither the disk nor any of its partitions may be boot/system, and none of its volumes may
///     be the Windows drive or hold a page file.
///  3. Its device path must differ from the disk that holds the Windows drive.
///  4. Exactly one such disk, 256 GB or smaller, must be connected.
///  5. The user types "ERASE &lt;disk number&gt;" after seeing name, serial, size and contents.
/// </summary>
internal static class UsbFormatSpike
{
    private const ushort BusTypeUsb = 7;
    private const ulong MB = 1024 * 1024;
    private const ulong MaxStickSize = 256UL * 1024 * MB;

    /// <summary>Read-only: shows which disk would be selected and why the others are not.</summary>
    public static void Check(Smp smp)
    {
        UsbStick.Select(smp, confirm: false);
        Log.Line("Dry run only. Nothing was changed.");
    }

    public static void Run(Smp smp)
    {
        var stick = UsbStick.Select(smp, confirm: true);
        if (stick is null)
        {
            Log.Line("Nothing was changed.");
            return;
        }

        Log.Section("Prepare: clear, initialize GPT, create a 1 GB partition");
        smp.Call(stick.Disk(), "Clear",
            ("RemoveData", true, CimType.Boolean),
            ("RemoveOEM", true, CimType.Boolean),
            ("ZeroOutEntireDisk", false, CimType.Boolean));
        smp.Call(stick.Disk(), "Initialize", ("PartitionStyle", SmpConst.StyleGpt, CimType.UInt16));
        smp.Call(stick.Disk(), "CreatePartition", ("Size", 1024 * MB, CimType.UInt64), ("AssignDriveLetter", false, CimType.Boolean));
        stick.LogLayout("prepared");

        foreach (var fs in new[] { "NTFS", "exFAT", "FAT32" })
        {
            SpikeVhd.Section(fs, () =>
            {
                Log.Section($"{fs}: quick, then full");
                Timed($"quick {fs}", () => smp.Call(stick.Volume(), "Format", ("FileSystem", fs, CimType.String), ("Full", false, CimType.Boolean)));
                Timed($"full {fs} (async, progress)", () => smp.CallWithProgress(stick.Volume(), "Format", ("FileSystem", fs, CimType.String), ("Full", true, CimType.Boolean)));
                Log.Line($"Volume file system now: {Log.Format(Smp.Prop(stick.Volume(), "FileSystem"))}");
            });
        }

        SpikeVhd.Section("NTFS full variants", () =>
        {
            Log.Section("NTFS full format variants");
            Timed("full NTFS, sync, AllocationUnitSize=4096", () => smp.Call(stick.Volume(), "Format",
                ("FileSystem", "NTFS", CimType.String), ("Full", true, CimType.Boolean), ("AllocationUnitSize", 4096u, CimType.UInt32)));
            Timed("full NTFS, sync, with label", () => smp.Call(stick.Volume(), "Format",
                ("FileSystem", "NTFS", CimType.String), ("FileSystemLabel", "WPTEST", CimType.String), ("Full", true, CimType.Boolean)));
        });

        SpikeVhd.Section("format.com fallback", () => FormatComFallback(smp, stick));

        SpikeVhd.Section("Leave the stick usable", () =>
        {
            Log.Section("Leave the stick usable: MBR, one exFAT volume labelled USB");
            smp.Call(stick.Disk(), "Clear",
                ("RemoveData", true, CimType.Boolean),
                ("RemoveOEM", true, CimType.Boolean),
                ("ZeroOutEntireDisk", false, CimType.Boolean));
            smp.Call(stick.Disk(), "Initialize", ("PartitionStyle", SmpConst.StyleMbr, CimType.UInt16));
            smp.Call(stick.Disk(), "CreatePartition", ("UseMaximumSize", true, CimType.Boolean), ("AssignDriveLetter", true, CimType.Boolean));
            smp.Call(stick.Volume(), "Format", ("FileSystem", "exFAT", CimType.String), ("FileSystemLabel", "USB", CimType.String), ("Full", false, CimType.Boolean));
            stick.LogLayout("final");
        });
    }

    private static void Timed(string label, Func<CimMethodResult?> call)
    {
        var sw = Stopwatch.StartNew();
        var result = call();
        Log.Line($"RESULT {label}: ReturnValue={Log.Format(result?.ReturnValue?.Value)} after {sw.Elapsed.TotalSeconds:F1} s");
    }

    /// <summary>Full NTFS format through format.com (no /Q), on a temporary drive letter.</summary>
    private static void FormatComFallback(Smp smp, UsbStick stick)
    {
        Log.Section("format.com full NTFS (fallback candidate)");
        smp.Call(stick.Partition(), "AddAccessPath", ("AssignDriveLetter", true, CimType.Boolean));
        Thread.Sleep(2000);
        var letter = Smp.Prop(stick.Partition(), "DriveLetter") is char c && c != '\0' ? c : (char?)null;
        if (letter is null)
        {
            Log.Line("No drive letter assigned; skipping format.com.");
            return;
        }

        // Re-check the guard right before handing a drive letter to an external tool.
        stick.Disk();
        if (!stick.OwnsDriveLetter(letter.Value))
        {
            throw new InvalidOperationException($"SAFETY GUARD: {letter}: does not belong to the USB stick. Nothing was run.");
        }

        var sw = Stopwatch.StartNew();
        var psi = new ProcessStartInfo("format.com", $"{letter}: /FS:NTFS /V:WPTEST /X")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi)!;
        p.StandardInput.WriteLine("Y");
        p.StandardInput.WriteLine();
        p.StandardInput.Close();
        var output = p.StandardOutput.ReadToEndAsync();
        if (!p.WaitForExit(TimeSpan.FromMinutes(15)))
        {
            p.Kill();
            Log.Line("format.com timed out after 15 minutes.");
        }

        Log.Line($"RESULT format.com full NTFS: exit={p.ExitCode} after {sw.Elapsed.TotalSeconds:F1} s");
        Log.Line($"format.com output (last lines): {string.Join(" | ", output.Result.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).TakeLast(6))}");
        Log.Line($"Volume file system now: {Log.Format(Smp.Prop(stick.Volume(), "FileSystem"))}");
        smp.Call(stick.Partition(), "RemoveAccessPath", ("AccessPath", $"{letter}:\\", CimType.String));
    }

    private sealed class UsbStick
    {
        private readonly Smp _smp;
        private readonly string _objectId;
        private readonly string _path;
        private readonly ulong _size;
        private readonly string _systemDiskPath;

        private UsbStick(Smp smp, CimInstance disk, string systemDiskPath)
        {
            _smp = smp;
            _objectId = (string)Smp.Prop(disk, "ObjectId")!;
            _path = (string)Smp.Prop(disk, "Path")!;
            _size = Convert.ToUInt64(Smp.Prop(disk, "Size"));
            _systemDiskPath = systemDiskPath;
        }

        public static UsbStick? Select(Smp smp, bool confirm)
        {
            Log.Section("Selecting the USB stick (nothing is changed in this step)");
            var windowsLetter = char.ToUpperInvariant(Environment.GetEnvironmentVariable("SystemRoot")![0]);
            var pagefileLetters = smp.Query("SELECT * FROM Win32_PageFileUsage", @"root\cimv2")
                .Select(p => char.ToUpperInvariant(((string)Smp.Prop(p, "Name")!)[0])).ToHashSet();
            var disks = smp.Query("SELECT * FROM MSFT_Disk");
            var partitions = smp.Query("SELECT * FROM MSFT_Partition");

            var systemDisk = partitions.FirstOrDefault(p => Smp.Prop(p, "DriveLetter") is char c && char.ToUpperInvariant(c) == windowsLetter);
            var systemDiskPath = (string?)Smp.Prop(systemDisk!, "DiskId");
            if (systemDiskPath is null)
            {
                Log.Line("SAFETY: could not determine which disk holds Windows. Stopping.");
                return null;
            }

            foreach (var d in disks)
            {
                Log.Line($"Disk {Smp.Prop(d, "Number")}: {Smp.Prop(d, "FriendlyName")} · BusType={Smp.Prop(d, "BusType")} · {Convert.ToUInt64(Smp.Prop(d, "Size")) / (1024 * MB)} GB · holds Windows={string.Equals((string?)Smp.Prop(d, "Path"), systemDiskPath, StringComparison.OrdinalIgnoreCase)}");
            }

            var candidates = disks.Where(d => IsSafeCandidate(d, partitions, systemDiskPath, windowsLetter, pagefileLetters, out _)).ToList();
            foreach (var d in disks.Except(candidates))
            {
                IsSafeCandidate(d, partitions, systemDiskPath, windowsLetter, pagefileLetters, out var why);
                Log.Line($"  Disk {Smp.Prop(d, "Number")} is NOT eligible: {why}");
            }

            if (candidates.Count != 1)
            {
                Log.Line($"SAFETY: expected exactly one eligible USB disk, found {candidates.Count}. Connect only the empty USB stick and run again.");
                return null;
            }

            var disk = candidates[0];
            if (!confirm)
            {
                Log.Line($"Would select Disk {Smp.Prop(disk, "Number")}: {Smp.Prop(disk, "FriendlyName")} (serial {Smp.Prop(disk, "SerialNumber")}, {Convert.ToUInt64(Smp.Prop(disk, "Size")) / MB} MB).");
                return null;
            }

            var number = Convert.ToUInt32(Smp.Prop(disk, "Number"));
            Console.WriteLine();
            Console.WriteLine("=================================================================");
            Console.WriteLine($" THIS USB DISK WILL BE COMPLETELY ERASED:");
            Console.WriteLine($"   Disk {number}: {Smp.Prop(disk, "FriendlyName")}");
            Console.WriteLine($"   Serial: {Smp.Prop(disk, "SerialNumber")}   Size: {Convert.ToUInt64(Smp.Prop(disk, "Size")) / MB} MB   Bus: USB");
            foreach (var p in partitions.Where(p => string.Equals((string?)Smp.Prop(p, "DiskId"), (string?)Smp.Prop(disk, "Path"), StringComparison.OrdinalIgnoreCase)))
            {
                Console.WriteLine($"   Partition {Smp.Prop(p, "PartitionNumber")}: {Convert.ToUInt64(Smp.Prop(p, "Size")) / MB} MB, letter {Log.Format(Smp.Prop(p, "DriveLetter"))}");
            }

            Console.WriteLine($" Your PC disk (with {windowsLetter}:) is NOT eligible and will not be touched.");
            Console.WriteLine("=================================================================");
            Console.Write($"Type  ERASE {number}  and press Enter to continue (anything else cancels): ");
            var answer = Console.ReadLine()?.Trim();
            if (answer != $"ERASE {number}")
            {
                Log.Line("Cancelled by the user.");
                return null;
            }

            Log.Line($"User confirmed ERASE {number} for {Smp.Prop(disk, "FriendlyName")} (serial {Smp.Prop(disk, "SerialNumber")}).");
            return new UsbStick(smp, disk, systemDiskPath);
        }

        private static bool IsSafeCandidate(CimInstance d, List<CimInstance> partitions, string systemDiskPath, char windowsLetter, HashSet<char> pagefileLetters, out string why)
        {
            var path = (string?)Smp.Prop(d, "Path");
            var parts = partitions.Where(p => string.Equals((string?)Smp.Prop(p, "DiskId"), path, StringComparison.OrdinalIgnoreCase)).ToList();
            var letters = parts.Select(p => Smp.Prop(p, "DriveLetter")).OfType<char>().Where(c => c != '\0').Select(char.ToUpperInvariant).ToList();

            why = Convert.ToUInt16(Smp.Prop(d, "BusType")) != BusTypeUsb ? "not a USB disk"
                : string.Equals(path, systemDiskPath, StringComparison.OrdinalIgnoreCase) ? "holds Windows"
                : Smp.Prop(d, "IsBoot") as bool? == true || Smp.Prop(d, "IsSystem") as bool? == true ? "boot/system disk"
                : parts.Any(p => Smp.Prop(p, "IsBoot") as bool? == true || Smp.Prop(p, "IsSystem") as bool? == true) ? "has a boot/system partition"
                : letters.Contains(windowsLetter) ? "has the Windows drive"
                : letters.Any(pagefileLetters.Contains) ? "holds a page file"
                : Convert.ToUInt64(Smp.Prop(d, "Size")) > MaxStickSize ? "larger than 256 GB"
                : Smp.Prop(d, "IsReadOnly") as bool? == true ? "read-only"
                : "";
            return why.Length == 0;
        }

        /// <summary>Re-reads the disk and re-checks every safety layer. Throws if anything changed.</summary>
        public CimInstance Disk()
        {
            var d = _smp.Query("SELECT * FROM MSFT_Disk").FirstOrDefault(x => (string?)Smp.Prop(x, "ObjectId") == _objectId)
                ?? throw new InvalidOperationException("SAFETY GUARD: the USB stick disappeared. Stopping.");
            var ok = Convert.ToUInt16(Smp.Prop(d, "BusType")) == BusTypeUsb
                && string.Equals((string?)Smp.Prop(d, "Path"), _path, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(_path, _systemDiskPath, StringComparison.OrdinalIgnoreCase)
                && Convert.ToUInt64(Smp.Prop(d, "Size")) == _size
                && Smp.Prop(d, "IsBoot") as bool? != true
                && Smp.Prop(d, "IsSystem") as bool? != true;
            return ok ? d : throw new InvalidOperationException("SAFETY GUARD: the target no longer matches the confirmed USB stick. Stopping.");
        }

        public List<CimInstance> Partitions()
        {
            Disk();
            return [.. _smp.Query("SELECT * FROM MSFT_Partition")
                .Where(p => string.Equals((string?)Smp.Prop(p, "DiskId"), _path, StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => Convert.ToUInt64(Smp.Prop(p, "Offset")))];
        }

        /// <summary>The last (data) partition on the stick.</summary>
        public CimInstance Partition() => Partitions().Last();

        public CimInstance Volume() =>
            _smp.Session.EnumerateAssociatedInstances(Smp.Ns, Partition(), "MSFT_PartitionToVolume", "MSFT_Volume", null, null).First();

        public bool OwnsDriveLetter(char letter) =>
            Partitions().Any(p => Smp.Prop(p, "DriveLetter") is char c && char.ToUpperInvariant(c) == char.ToUpperInvariant(letter));

        public void LogLayout(string title)
        {
            var d = Disk();
            Log.Line($"--- Layout ({title}): style={Smp.Prop(d, "PartitionStyle")} size={Smp.Prop(d, "Size")}");
            foreach (var p in Partitions())
            {
                Log.Line($"    #{Smp.Prop(p, "PartitionNumber")} Offset={Smp.Prop(p, "Offset")} Size={Smp.Prop(p, "Size")} GptType={Smp.Prop(p, "GptType")} MbrType={Smp.Prop(p, "MbrType")} Letter={Log.Format(Smp.Prop(p, "DriveLetter"))}");
            }
        }
    }
}
