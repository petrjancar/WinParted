using Microsoft.Management.Infrastructure;
using Microsoft.Win32;
using WinParted.Core.Model;
using WinParted.Storage.Cim;

namespace WinParted.Storage;

/// <summary>
/// Facts that protection depends on (design D6, task 7.2). Anything that can't be read is reported
/// as unknown (null), which the protection rules treat as protected.
/// </summary>
internal sealed class SystemFacts
{
    public char WindowsLetter { get; init; }

    /// <summary>Letters holding a page file or dedicated dump file; null if unknown.</summary>
    public HashSet<char>? PagefileLetters { get; init; }

    /// <summary>BitLocker state by volume path (\\?\Volume{...}\); null if unknown.</summary>
    public Dictionary<string, BitLockerState>? BitLockerByVolumePath { get; init; }

    /// <summary>Storage Spaces membership by disk number; null if unknown.</summary>
    public Dictionary<uint, bool>? PooledByDiskNumber { get; init; }

    /// <summary>Disk numbers of removable media (CannotPoolReason 4).</summary>
    public HashSet<uint> RemovableDiskNumbers { get; init; } = [];

    /// <summary>
    /// Letters used by drives SMP doesn't list (network, optical). Letters of disk volumes come from
    /// SMP itself, because Windows' drive list can still show a letter just after it was removed.
    /// </summary>
    public HashSet<char> OtherDriveLetters { get; init; } = [];

    public static SystemFacts Read(Smp smp)
    {
        var (pooled, removable) = ReadPhysicalDisks(smp);
        return new SystemFacts
        {
        WindowsLetter = char.ToUpperInvariant(Environment.GetEnvironmentVariable("SystemRoot")?[0] ?? 'C'),
        PagefileLetters = ReadPagefileLetters(smp),
        BitLockerByVolumePath = ReadBitLocker(smp),
        PooledByDiskNumber = pooled,
        RemovableDiskNumbers = removable,
        OtherDriveLetters = [.. DriveInfo.GetDrives()
            .Where(d => d.DriveType is not (DriveType.Fixed or DriveType.Removable or DriveType.NoRootDirectory))
            .Select(d => char.ToUpperInvariant(d.Name[0]))],
        };
    }

    private static HashSet<char>? ReadPagefileLetters(Smp smp)
    {
        try
        {
            var letters = smp.Query("Win32_PageFileUsage", Smp.Cimv2)
                .Select(p => Smp.Str(p, "Name"))
                .Where(n => n is { Length: > 1 } && n[1] == ':')
                .Select(n => char.ToUpperInvariant(n![0]))
                .ToHashSet();

            using var crash = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\CrashControl");
            if (crash?.GetValue("DedicatedDumpFile") is string dump && dump.Length > 1 && dump[1] == ':')
            {
                letters.Add(char.ToUpperInvariant(dump[0]));
            }

            return letters;
        }
        catch (Exception ex) when (ex is CimException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return null;
        }
    }

    private static Dictionary<string, BitLockerState>? ReadBitLocker(Smp smp)
    {
        List<CimInstance> volumes;
        try
        {
            volumes = smp.Query("Win32_EncryptableVolume", Smp.BitLockerNamespace);
        }
        catch (CimException ex) when (ex.NativeErrorCode is NativeErrorCode.InvalidNamespace or NativeErrorCode.InvalidClass)
        {
            // BitLocker isn't part of this Windows installation: nothing can be encrypted.
            return new Dictionary<string, BitLockerState>(StringComparer.OrdinalIgnoreCase);
        }
        catch (CimException)
        {
            return null;
        }

        var result = new Dictionary<string, BitLockerState>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in volumes)
        {
            if (Smp.Str(v, "DeviceID") is not { } path)
            {
                continue;
            }

            try
            {
                var lockStatus = smp.Session.InvokeMethod(Smp.BitLockerNamespace, v, "GetLockStatus", new CimMethodParametersCollection())
                    .OutParameters["LockStatus"]?.Value is { } ls ? Convert.ToUInt32(ls) : 0u;
                var protection = Smp.Prop(v, "ProtectionStatus") is { } ps ? Convert.ToUInt32(ps) : 0u;
                result[path] = lockStatus == 1 ? BitLockerState.Locked
                    : protection == 1 ? BitLockerState.Unlocked
                    : protection == 2 ? BitLockerState.Unknown
                    : BitLockerState.NotEncrypted;
            }
            catch (CimException)
            {
                result[path] = BitLockerState.Unknown;
            }
        }

        return result;
    }

    private static (Dictionary<uint, bool>? Pooled, HashSet<uint> Removable) ReadPhysicalDisks(Smp smp)
    {
        var removable = new HashSet<uint>();
        try
        {
            var result = new Dictionary<uint, bool>();
            foreach (var pd in smp.Query("MSFT_PhysicalDisk"))
            {
                if (uint.TryParse(Smp.Str(pd, "DeviceId"), out var number))
                {
                    // CannotPoolReason 2 = "In a pool" (docs/spikes/smp-behavior.md).
                    var reasons = Smp.Prop(pd, "CannotPoolReason") as ushort[] ?? [];
                    result[number] = reasons.Contains((ushort)2);
                    if (reasons.Contains((ushort)4))
                    {
                        // CannotPoolReason 4 = "Removable media".
                        removable.Add(number);
                    }
                }
            }

            return (result, removable);
        }
        catch (CimException)
        {
            return (null, removable);
        }
    }
}
