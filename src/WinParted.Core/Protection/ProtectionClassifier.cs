using WinParted.Core.Model;

namespace WinParted.Core.Protection;

/// <summary>Why a partition is protected. Several can apply at once.</summary>
[Flags]
public enum PartitionProtectionFlags
{
    None = 0,
    EfiSystem = 1 << 0,
    MicrosoftReserved = 1 << 1,
    Recovery = 1 << 2,

    /// <summary>The partition the computer starts from, e.g. MBR "System Reserved".</summary>
    SystemPartition = 1 << 3,

    /// <summary>Holds the running Windows installation.</summary>
    WindowsVolume = 1 << 4,

    /// <summary>Holds the pagefile or crash dump file.</summary>
    PagefileVolume = 1 << 5,
    BitLockerLocked = 1 << 6,

    /// <summary>A protection fact could not be determined (fail closed).</summary>
    ProtectionUnknown = 1 << 7,
}

/// <summary>Why a whole disk is protected.</summary>
[Flags]
public enum DiskProtectionFlags
{
    None = 0,

    /// <summary>Holds Windows or the partitions it starts from.</summary>
    SystemDisk = 1 << 0,
    DynamicDisk = 1 << 1,
    StorageSpaces = 1 << 2,
    ReadOnly = 1 << 3,

    /// <summary>Storage Spaces membership could not be determined (fail closed).</summary>
    ProtectionUnknown = 1 << 4,
}

/// <summary>The role shown next to a lock badge, most important first.</summary>
public enum ProtectionRole
{
    EfiSystem,
    MicrosoftReserved,
    Recovery,
    SystemPartition,
    WindowsVolume,
    PagefileVolume,
    BitLockerLocked,
    ProtectionUnknown,
    SystemDisk,
    DynamicDisk,
    StorageSpaces,
    ReadOnlyDisk,
}

public sealed record PartitionProtection(PartitionProtectionFlags Flags)
{
    /// <summary>Flags that block every modifying operation on the partition.</summary>
    public const PartitionProtectionFlags FullLockFlags =
        PartitionProtectionFlags.EfiSystem | PartitionProtectionFlags.MicrosoftReserved |
        PartitionProtectionFlags.Recovery | PartitionProtectionFlags.SystemPartition |
        PartitionProtectionFlags.BitLockerLocked | PartitionProtectionFlags.ProtectionUnknown;

    /// <summary>Flags that block Delete, Format and Change drive letter only.</summary>
    public const PartitionProtectionFlags RestrictedFlags =
        PartitionProtectionFlags.WindowsVolume | PartitionProtectionFlags.PagefileVolume;

    public bool IsProtected => Flags != PartitionProtectionFlags.None;

    public bool IsFullyLocked => (Flags & FullLockFlags) != 0;

    public bool IsRestricted => (Flags & RestrictedFlags) != 0;

    /// <summary>The most important role, for the lock badge description; null when unprotected.</summary>
    public ProtectionRole? Role => Flags switch
    {
        _ when Flags.HasFlag(PartitionProtectionFlags.EfiSystem) => ProtectionRole.EfiSystem,
        _ when Flags.HasFlag(PartitionProtectionFlags.MicrosoftReserved) => ProtectionRole.MicrosoftReserved,
        _ when Flags.HasFlag(PartitionProtectionFlags.Recovery) => ProtectionRole.Recovery,
        _ when Flags.HasFlag(PartitionProtectionFlags.SystemPartition) => ProtectionRole.SystemPartition,
        _ when Flags.HasFlag(PartitionProtectionFlags.BitLockerLocked) => ProtectionRole.BitLockerLocked,
        _ when Flags.HasFlag(PartitionProtectionFlags.ProtectionUnknown) => ProtectionRole.ProtectionUnknown,
        _ when Flags.HasFlag(PartitionProtectionFlags.WindowsVolume) => ProtectionRole.WindowsVolume,
        _ when Flags.HasFlag(PartitionProtectionFlags.PagefileVolume) => ProtectionRole.PagefileVolume,
        _ => null,
    };
}

public sealed record DiskProtection(DiskProtectionFlags Flags)
{
    /// <summary>Flags that make the whole disk and everything on it read-only.</summary>
    public const DiskProtectionFlags WholeDiskLockFlags =
        DiskProtectionFlags.DynamicDisk | DiskProtectionFlags.StorageSpaces |
        DiskProtectionFlags.ReadOnly | DiskProtectionFlags.ProtectionUnknown;

    public bool IsProtected => Flags != DiskProtectionFlags.None;

    public bool IsWholeDiskLocked => (Flags & WholeDiskLockFlags) != 0;

    public bool IsSystemDisk => Flags.HasFlag(DiskProtectionFlags.SystemDisk);

    public ProtectionRole? Role => Flags switch
    {
        _ when Flags.HasFlag(DiskProtectionFlags.ReadOnly) => ProtectionRole.ReadOnlyDisk,
        _ when Flags.HasFlag(DiskProtectionFlags.DynamicDisk) => ProtectionRole.DynamicDisk,
        _ when Flags.HasFlag(DiskProtectionFlags.StorageSpaces) => ProtectionRole.StorageSpaces,
        _ when Flags.HasFlag(DiskProtectionFlags.ProtectionUnknown) => ProtectionRole.ProtectionUnknown,
        _ when Flags.HasFlag(DiskProtectionFlags.SystemDisk) => ProtectionRole.SystemDisk,
        _ => null,
    };
}

/// <summary>
/// Decides which disks and partitions are protected (design D6, spec protected-storage). Each item
/// is checked against several independent signals, and anything that can't be determined is
/// treated as protected.
/// </summary>
public static class ProtectionClassifier
{
    public static PartitionProtection Classify(Partition partition)
    {
        var flags = PartitionProtectionFlags.None;

        if (KnownPartitionTypes.IsEfiSystem(partition))
        {
            flags |= PartitionProtectionFlags.EfiSystem;
        }

        if (KnownPartitionTypes.IsMicrosoftReserved(partition))
        {
            flags |= PartitionProtectionFlags.MicrosoftReserved;
        }

        if (KnownPartitionTypes.IsRecovery(partition))
        {
            flags |= PartitionProtectionFlags.Recovery;
        }

        if (partition.IsSystem)
        {
            flags |= PartitionProtectionFlags.SystemPartition;
        }

        if (partition.IsBoot || partition.Volume?.IsWindowsVolume == true)
        {
            flags |= PartitionProtectionFlags.WindowsVolume;
        }

        if (partition.Volume is { } volume)
        {
            switch (volume.HasPagefileOrDump)
            {
                case true:
                    flags |= PartitionProtectionFlags.PagefileVolume;
                    break;
                case null:
                    flags |= PartitionProtectionFlags.ProtectionUnknown;
                    break;
            }

            switch (volume.BitLocker)
            {
                case BitLockerState.Locked:
                    flags |= PartitionProtectionFlags.BitLockerLocked;
                    break;
                case BitLockerState.Unknown:
                    flags |= PartitionProtectionFlags.ProtectionUnknown;
                    break;
            }
        }

        return new PartitionProtection(flags);
    }

    public static DiskProtection Classify(Disk disk)
    {
        var flags = DiskProtectionFlags.None;

        var holdsSystem = disk.IsSystem || disk.IsBoot || disk.Partitions.Any(p =>
            p.IsSystem || p.IsBoot || p.Volume?.IsWindowsVolume == true);
        if (holdsSystem)
        {
            flags |= DiskProtectionFlags.SystemDisk;
        }

        if (disk.IsDynamic)
        {
            flags |= DiskProtectionFlags.DynamicDisk;
        }

        switch (disk.IsStorageSpacesMember)
        {
            case true:
                flags |= DiskProtectionFlags.StorageSpaces;
                break;
            case null:
                flags |= DiskProtectionFlags.ProtectionUnknown;
                break;
        }

        if (disk.Partitions.Any(KnownPartitionTypes.IsStorageSpaces))
        {
            flags |= DiskProtectionFlags.StorageSpaces;
        }

        if (disk.IsReadOnly)
        {
            flags |= DiskProtectionFlags.ReadOnly;
        }

        return new DiskProtection(flags);
    }
}
