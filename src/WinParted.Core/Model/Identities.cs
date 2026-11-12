namespace WinParted.Core.Model;

/// <summary>
/// Stable disk identity (SMP <c>MSFT_Disk.ObjectId</c>). Disk numbers are display-only and can be
/// reassigned when disks are connected or removed, so they are never used to identify a disk.
/// </summary>
public readonly record struct DiskId(string Value)
{
    public override string ToString() => Value;
}

/// <summary>A partition is identified by its disk and its starting offset in bytes.</summary>
public readonly record struct PartitionId(DiskId Disk, ulong Offset);

/// <summary>Stable volume identity (SMP <c>MSFT_Volume.UniqueId</c>, e.g. <c>\\?\Volume{GUID}\</c>).</summary>
public readonly record struct VolumeId(string Value)
{
    public override string ToString() => Value;
}
