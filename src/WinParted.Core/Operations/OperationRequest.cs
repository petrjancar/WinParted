using WinParted.Core.Actions;
using WinParted.Core.Model;

namespace WinParted.Core.Operations;

/// <summary>Options for formatting a volume.</summary>
/// <param name="AllocationUnitSize">Cluster size in bytes; null means the Windows default.</param>
public sealed record FormatOptions(FileSystemKind FileSystem, string? Label, bool Quick = true, uint? AllocationUnitSize = null);

/// <summary>
/// A confirmed operation. It carries the target's identity and a fingerprint of the properties
/// the user saw when confirming; the runner re-checks both right before executing (design D4).
/// </summary>
public abstract record OperationRequest(ActionTarget Target, TargetFingerprint Fingerprint)
{
    public abstract ActionKind Kind { get; }
}

public sealed record InitializeDiskRequest(DiskTarget Disk, TargetFingerprint Fingerprint, PartitionStyle Style)
    : OperationRequest(Disk, Fingerprint)
{
    public override ActionKind Kind => ActionKind.Initialize;
}

public sealed record SetDiskOnlineRequest(DiskTarget Disk, TargetFingerprint Fingerprint, bool Online)
    : OperationRequest(Disk, Fingerprint)
{
    public override ActionKind Kind => Online ? ActionKind.BringOnline : ActionKind.TakeOffline;
}

public sealed record ClearDiskRequest(DiskTarget Disk, TargetFingerprint Fingerprint)
    : OperationRequest(Disk, Fingerprint)
{
    public override ActionKind Kind => ActionKind.ClearDisk;
}

/// <param name="Format">Null creates the partition without a file system or drive letter.</param>
public sealed record CreatePartitionRequest(
    UnallocatedTarget Region,
    TargetFingerprint Fingerprint,
    ulong Size,
    FormatOptions? Format,
    char? DriveLetter)
    : OperationRequest(Region, Fingerprint)
{
    public override ActionKind Kind => ActionKind.CreatePartition;
}

public sealed record DeletePartitionRequest(PartitionTarget Partition, TargetFingerprint Fingerprint)
    : OperationRequest(Partition, Fingerprint)
{
    public override ActionKind Kind => ActionKind.DeletePartition;
}

public sealed record FormatVolumeRequest(PartitionTarget Partition, TargetFingerprint Fingerprint, FormatOptions Options)
    : OperationRequest(Partition, Fingerprint)
{
    public override ActionKind Kind => ActionKind.FormatVolume;
}

public sealed record ResizePartitionRequest(PartitionTarget Partition, TargetFingerprint Fingerprint, ulong NewSize, bool IsShrink)
    : OperationRequest(Partition, Fingerprint)
{
    public override ActionKind Kind => IsShrink ? ActionKind.ShrinkVolume : ActionKind.ExtendVolume;
}

/// <param name="Letter">The new letter, or null to remove the drive letter.</param>
public sealed record SetDriveLetterRequest(PartitionTarget Partition, TargetFingerprint Fingerprint, char? Letter)
    : OperationRequest(Partition, Fingerprint)
{
    public override ActionKind Kind => ActionKind.ChangeDriveLetter;
}

public sealed record SetLabelRequest(PartitionTarget Partition, TargetFingerprint Fingerprint, string Label)
    : OperationRequest(Partition, Fingerprint)
{
    public override ActionKind Kind => ActionKind.ChangeLabel;
}
