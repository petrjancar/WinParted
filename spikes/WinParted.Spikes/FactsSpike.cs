using Microsoft.Management.Infrastructure;
using Microsoft.Win32;

namespace WinParted.Spikes;

/// <summary>
/// Read-only: dumps everything WinParted needs to read (tasks 7.1 and 7.2). Changes nothing.
/// </summary>
internal static class FactsSpike
{
    public static void Run(Smp smp)
    {
        Log.Section("MSFT_Disk (all disks)");
        foreach (var d in smp.Query("SELECT * FROM MSFT_Disk"))
        {
            Log.Instance(d, $"Disk {Smp.Prop(d, "Number")}");
        }

        Log.Section("MSFT_Partition (all partitions)");
        var partitions = smp.Query("SELECT * FROM MSFT_Partition");
        foreach (var p in partitions)
        {
            Log.Instance(p, $"Disk {Smp.Prop(p, "DiskNumber")} partition {Smp.Prop(p, "PartitionNumber")}");
        }

        Log.Section("MSFT_Volume (all volumes)");
        foreach (var v in smp.Query("SELECT * FROM MSFT_Volume"))
        {
            Log.Instance(v, $"Volume {Smp.Prop(v, "DriveLetter")} {Smp.Prop(v, "FileSystemLabel")}");
        }

        Log.Section("Partition -> Volume association (first 20)");
        foreach (var p in partitions.Take(20))
        {
            try
            {
                var vols = smp.Session.EnumerateAssociatedInstances(Smp.Ns, p, "MSFT_PartitionToVolume", "MSFT_Volume", null, null).ToList();
                Log.Line($"Disk {Smp.Prop(p, "DiskNumber")} #{Smp.Prop(p, "PartitionNumber")} -> {string.Join(", ", vols.Select(v => Log.Format(Smp.Prop(v, "UniqueId"))))}");
            }
            catch (CimException ex)
            {
                Smp.LogCimException("ASSOC", ex);
            }
        }

        Log.Section("Supported file systems and shrink limits (read-only queries)");
        foreach (var v in smp.Query("SELECT * FROM MSFT_Volume").Where(v => Smp.Prop(v, "DriveLetter") is char c && c != '\0'))
        {
            smp.Call(v, "GetSupportedFileSystems");
            smp.Call(v, "GetSupportedClusterSizes", ("FileSystem", "NTFS", CimType.String));
        }

        foreach (var p in partitions.Where(p => Smp.Prop(p, "DriveLetter") is char c && c != '\0'))
        {
            smp.Call(p, "GetSupportedSize");
        }

        Log.Section("MSFT_PhysicalDisk and MSFT_StoragePool (Storage Spaces)");
        foreach (var pd in smp.Query("SELECT * FROM MSFT_PhysicalDisk"))
        {
            Log.Line($"PhysicalDisk DeviceId={Smp.Prop(pd, "DeviceId")} FriendlyName={Smp.Prop(pd, "FriendlyName")} CanPool={Smp.Prop(pd, "CanPool")} CannotPoolReason={Log.Format(Smp.Prop(pd, "CannotPoolReason"))} BusType={Smp.Prop(pd, "BusType")}");
        }

        foreach (var pool in smp.Query("SELECT * FROM MSFT_StoragePool"))
        {
            Log.Line($"StoragePool FriendlyName={Smp.Prop(pool, "FriendlyName")} IsPrimordial={Smp.Prop(pool, "IsPrimordial")} ObjectId={Smp.Prop(pool, "ObjectId")}");
        }

        Log.Section("MSFT_DiskImage (attached VHD/ISO)");
        foreach (var img in smp.Query("SELECT * FROM MSFT_DiskImage"))
        {
            Log.Instance(img);
        }

        Log.Section("System facts");
        Log.Line($"SystemRoot={Environment.GetEnvironmentVariable("SystemRoot")}");
        Log.Line($"Logical drives (this session)={string.Join(" ", Environment.GetLogicalDrives())}");
        foreach (var pf in smp.Query("SELECT * FROM Win32_PageFileUsage", @"root\cimv2"))
        {
            Log.Line($"PageFile Name={Smp.Prop(pf, "Name")} AllocatedBaseSize={Smp.Prop(pf, "AllocatedBaseSize")}");
        }

        using (var crash = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\CrashControl"))
        {
            Log.Line($"CrashControl DumpFile={crash?.GetValue("DumpFile")} DedicatedDumpFile={crash?.GetValue("DedicatedDumpFile")} CrashDumpEnabled={crash?.GetValue("CrashDumpEnabled")}");
        }

        using (var san = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\partmgr\Parameters"))
        {
            Log.Line($"SAN policy (partmgr SanPolicy)={san?.GetValue("SanPolicy") ?? "<not set: OS default>"}");
        }

        Log.Section("BitLocker (Win32_EncryptableVolume)");
        const string bitLockerNs = @"root\cimv2\Security\MicrosoftVolumeEncryption";
        var encryptable = smp.Query("SELECT * FROM Win32_EncryptableVolume", bitLockerNs);
        if (encryptable.Count == 0)
        {
            Log.Line("No instances (BitLocker not available, or not elevated).");
        }

        foreach (var ev in encryptable)
        {
            Log.Line($"EncryptableVolume DriveLetter={Smp.Prop(ev, "DriveLetter")} DeviceID={Smp.Prop(ev, "DeviceID")} ProtectionStatus={Smp.Prop(ev, "ProtectionStatus")}");
            try
            {
                var r = smp.Session.InvokeMethod(bitLockerNs, ev, "GetLockStatus", new CimMethodParametersCollection());
                Log.Line($"    GetLockStatus -> ReturnValue={r.ReturnValue?.Value} LockStatus={r.OutParameters["LockStatus"]?.Value}");
            }
            catch (CimException ex)
            {
                Smp.LogCimException("    GetLockStatus", ex);
            }
        }
    }
}
