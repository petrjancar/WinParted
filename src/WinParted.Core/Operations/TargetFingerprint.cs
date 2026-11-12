using WinParted.Core.Actions;
using WinParted.Core.Model;

namespace WinParted.Core.Operations;

/// <summary>
/// The properties of a target that an operation depends on. If any of them differ between the
/// moment the user confirmed and the moment of execution, the operation is aborted (design D4).
/// The disk number is included because the user confirmed what they saw ("Type 3 to clear Disk 3").
/// </summary>
public sealed record TargetFingerprint(
    int DiskNumber,
    ulong DiskSize,
    PartitionStyle Style,
    bool IsOffline,
    int PartitionCount,
    ulong? Offset = null,
    ulong? Size = null,
    Guid? UniqueGuid = null,
    FileSystemKind? FileSystem = null,
    string? Label = null,
    char? DriveLetter = null)
{
    /// <summary>The fingerprint of the target in the snapshot, or null if the target doesn't exist.</summary>
    public static TargetFingerprint? Of(StorageSnapshot snapshot, ActionTarget target)
    {
        switch (target)
        {
            case DiskTarget t when snapshot.FindDisk(t.Disk) is { } disk:
                return ForDisk(disk);

            case UnallocatedTarget t when snapshot.FindDisk(t.Disk) is { } disk
                && disk.UnallocatedSegments.FirstOrDefault(s => s.Offset == t.Offset) is { } region:
                return ForDisk(disk) with { Offset = region.Offset, Size = region.Size };

            case PartitionTarget t when snapshot.FindDisk(t.Partition.Disk) is { } disk
                && snapshot.FindPartition(t.Partition) is { } p:
                return ForDisk(disk) with
                {
                    Offset = p.Offset,
                    Size = p.Size,
                    UniqueGuid = p.UniqueGuid,
                    FileSystem = p.Volume?.FileSystem,
                    Label = p.Volume?.Label,
                    DriveLetter = p.DriveLetter,
                };

            default:
                return null;
        }
    }

    private static TargetFingerprint ForDisk(Disk disk) =>
        new(disk.Number, disk.Size, disk.Style, disk.IsOffline, disk.Partitions.Length);
}
