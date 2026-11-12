using System.Collections.Immutable;
using WinParted.Core.Model;
using WinParted.Core.Protection;
using WinParted.Core.Selections;
using WinParted.Core.Text;

namespace WinParted.Core.Actions;

public enum ActionKind
{
    // Disk
    Initialize,
    BringOnline,
    TakeOffline,
    ClearDisk,

    // Unallocated region
    CreatePartition,

    // Partition / volume
    FormatVolume,
    DeletePartition,
    ShrinkVolume,
    ExtendVolume,
    ChangeDriveLetter,
    ChangeLabel,
}

/// <summary>What an action applies to. Identities only, never disk numbers.</summary>
public abstract record ActionTarget;

public sealed record DiskTarget(DiskId Disk) : ActionTarget;

public sealed record PartitionTarget(PartitionId Partition) : ActionTarget;

public sealed record UnallocatedTarget(DiskId Disk, ulong Offset) : ActionTarget;

public sealed record ActionState(ActionKind Kind, Reason? DisabledReason)
{
    public bool IsEnabled => DisabledReason is null;
}

/// <summary>
/// The single place that decides whether an action is allowed and, if not, why (design D5).
/// Used by the UI to show actions and by the operation runner right before executing, so the two
/// can never disagree. Rules are checked in a fixed order: busy, whole-disk locks, offline,
/// protection, then operation-specific preconditions.
/// </summary>
public static class ActionAvailability
{
    private static readonly ImmutableArray<ActionKind> PartitionActions =
    [
        ActionKind.FormatVolume,
        ActionKind.DeletePartition,
        ActionKind.ShrinkVolume,
        ActionKind.ExtendVolume,
        ActionKind.ChangeDriveLetter,
        ActionKind.ChangeLabel,
    ];

    /// <summary>All actions relevant to the current selection, enabled or not.</summary>
    public static ImmutableArray<ActionState> ForSelection(StorageSnapshot snapshot, Selection selection, bool isBusy)
    {
        var target = ToTarget(selection);
        return target is null
            ? []
            : [.. RelevantActions(snapshot, target).Select(kind => Evaluate(snapshot, kind, target, isBusy))];
    }

    public static ActionTarget? ToTarget(Selection selection) => selection switch
    {
        { Segment: PartitionRef p } => new PartitionTarget(p.Id),
        { Segment: UnallocatedRef u } => new UnallocatedTarget(u.Disk, u.Offset),
        { Disk: DiskId d } => new DiskTarget(d),
        _ => null,
    };

    public static ImmutableArray<ActionKind> RelevantActions(StorageSnapshot snapshot, ActionTarget target) => target switch
    {
        DiskTarget d =>
        [
            ActionKind.Initialize,
            snapshot.FindDisk(d.Disk)?.IsOffline == true ? ActionKind.BringOnline : ActionKind.TakeOffline,
            ActionKind.ClearDisk,
        ],
        UnallocatedTarget => [ActionKind.CreatePartition],
        PartitionTarget => PartitionActions,
        _ => [],
    };

    public static ActionState Evaluate(StorageSnapshot snapshot, ActionKind kind, ActionTarget target, bool isBusy) =>
        new(kind, Check(snapshot, kind, target, isBusy));

    private static Reason? Check(StorageSnapshot snapshot, ActionKind kind, ActionTarget target, bool isBusy)
    {
        if (isBusy)
        {
            return Reason.Of(ReasonCode.OperationInProgress);
        }

        return target switch
        {
            DiskTarget t => snapshot.FindDisk(t.Disk) is { } disk
                ? CheckDisk(kind, disk)
                : Reason.Of(ReasonCode.TargetChanged),
            UnallocatedTarget t => snapshot.FindDisk(t.Disk) is { } disk
                && disk.UnallocatedSegments.FirstOrDefault(s => s.Offset == t.Offset) is { } region
                ? CheckCreate(kind, disk, region)
                : Reason.Of(ReasonCode.TargetChanged),
            PartitionTarget t => snapshot.FindDisk(t.Partition.Disk) is { } disk
                && snapshot.FindPartition(t.Partition) is { } partition
                ? CheckPartition(kind, disk, partition, snapshot.Facts)
                : Reason.Of(ReasonCode.TargetChanged),
            _ => throw new ArgumentOutOfRangeException(nameof(target)),
        };
    }

    // ----- Shared checks -------------------------------------------------------------------------

    private static Reason? WholeDiskLock(DiskProtection protection)
    {
        var f = protection.Flags;
        if (f.HasFlag(DiskProtectionFlags.ReadOnly))
        {
            return Reason.Of(ReasonCode.DiskReadOnly);
        }

        if (f.HasFlag(DiskProtectionFlags.DynamicDisk))
        {
            return Reason.Of(ReasonCode.DynamicDisk);
        }

        if (f.HasFlag(DiskProtectionFlags.StorageSpaces))
        {
            return Reason.Of(ReasonCode.StorageSpaces);
        }

        return f.HasFlag(DiskProtectionFlags.ProtectionUnknown) ? Reason.Of(ReasonCode.ProtectionUnknown) : null;
    }

    private static Reason? PartitionLock(PartitionProtection protection, bool restrictedApplies)
    {
        var f = protection.Flags;
        if (f.HasFlag(PartitionProtectionFlags.EfiSystem) || f.HasFlag(PartitionProtectionFlags.SystemPartition))
        {
            return Reason.Of(ReasonCode.SystemPartition);
        }

        if (f.HasFlag(PartitionProtectionFlags.MicrosoftReserved))
        {
            return Reason.Of(ReasonCode.MicrosoftReserved);
        }

        if (f.HasFlag(PartitionProtectionFlags.Recovery))
        {
            return Reason.Of(ReasonCode.RecoveryPartition);
        }

        if (f.HasFlag(PartitionProtectionFlags.BitLockerLocked))
        {
            return Reason.Of(ReasonCode.BitLockerLocked);
        }

        if (f.HasFlag(PartitionProtectionFlags.ProtectionUnknown))
        {
            return Reason.Of(ReasonCode.ProtectionUnknown);
        }

        if (!restrictedApplies)
        {
            return null;
        }

        if (f.HasFlag(PartitionProtectionFlags.WindowsVolume))
        {
            return Reason.Of(ReasonCode.WindowsVolume);
        }

        return f.HasFlag(PartitionProtectionFlags.PagefileVolume) ? Reason.Of(ReasonCode.PagefileVolume) : null;
    }

    // ----- Disk actions --------------------------------------------------------------------------

    private static Reason? CheckDisk(ActionKind kind, Disk disk)
    {
        var protection = ProtectionClassifier.Classify(disk);

        // Bring online writes no data, so a read-only flag doesn't block it (spec protected-storage,
        // "Offline and read-only disk"). Dynamic and Storage Spaces disks stay fully locked.
        var lockCheck = kind == ActionKind.BringOnline
            ? protection with { Flags = protection.Flags & ~DiskProtectionFlags.ReadOnly }
            : protection;
        if (WholeDiskLock(lockCheck) is { } locked)
        {
            return locked;
        }

        return kind switch
        {
            ActionKind.BringOnline => null,
            ActionKind.TakeOffline => protection.IsSystemDisk ? Reason.Of(ReasonCode.SystemDisk)
                : disk.IsRemovableMedia ? Reason.Of(ReasonCode.RemovableMediaOffline)
                : null,
            ActionKind.Initialize => disk.IsOffline ? Reason.Of(ReasonCode.DiskOffline)
                : protection.IsSystemDisk ? Reason.Of(ReasonCode.SystemDisk)
                : disk.Style != PartitionStyle.Uninitialized ? Reason.Of(ReasonCode.AlreadyInitialized)
                : null,
            ActionKind.ClearDisk => disk.IsOffline ? Reason.Of(ReasonCode.DiskOffline)
                : protection.IsSystemDisk ? Reason.Of(ReasonCode.SystemDisk)
                : disk.Style == PartitionStyle.Uninitialized ? Reason.Of(ReasonCode.DiskAlreadyEmpty)
                : null,
            _ => throw new ArgumentException($"{kind} doesn't apply to a disk.", nameof(kind)),
        };
    }

    // ----- Create partition ----------------------------------------------------------------------

    private static Reason? CheckCreate(ActionKind kind, Disk disk, UnallocatedSegment region)
    {
        if (kind != ActionKind.CreatePartition)
        {
            throw new ArgumentException($"{kind} doesn't apply to unallocated space.", nameof(kind));
        }

        if (WholeDiskLock(ProtectionClassifier.Classify(disk)) is { } locked)
        {
            return locked;
        }

        if (disk.IsOffline)
        {
            return Reason.Of(ReasonCode.DiskOffline);
        }

        return disk.Style == PartitionStyle.Mbr && IsMbrPrimaryTableFull(disk, region)
            ? Reason.Of(ReasonCode.MbrPartitionLimit)
            : null;
    }

    /// <summary>
    /// An MBR disk holds at most four primary entries (an extended container counts as one).
    /// Free space inside an extended container can still take logical partitions. SMP creates the
    /// container automatically for the fourth partition and makes it span all remaining space, so the
    /// limit only shows on disks partitioned by other tools (spike 2.3).
    /// </summary>
    private static bool IsMbrPrimaryTableFull(Disk disk, UnallocatedSegment region)
    {
        var containers = disk.Partitions.Where(KnownPartitionTypes.IsMbrExtendedContainer).ToList();
        if (containers.Any(c => c.Offset <= region.Offset && region.End <= c.End))
        {
            return false;
        }

        var primaryCount = disk.Partitions.Count(p =>
            KnownPartitionTypes.IsMbrExtendedContainer(p) ||
            !containers.Any(c => c.Offset <= p.Offset && p.End <= c.End));
        return primaryCount >= 4;
    }

    // ----- Partition actions ---------------------------------------------------------------------

    private static Reason? CheckPartition(ActionKind kind, Disk disk, Partition partition, SystemFacts facts)
    {
        if (WholeDiskLock(ProtectionClassifier.Classify(disk)) is { } locked)
        {
            return locked;
        }

        if (disk.IsOffline)
        {
            return Reason.Of(ReasonCode.DiskOffline);
        }

        var restrictedApplies = kind is ActionKind.DeletePartition or ActionKind.FormatVolume or ActionKind.ChangeDriveLetter;
        if (PartitionLock(ProtectionClassifier.Classify(partition), restrictedApplies) is { } protectedReason)
        {
            return protectedReason;
        }

        var content = partition.Content;
        return kind switch
        {
            ActionKind.DeletePartition => null,
            ActionKind.FormatVolume => content == PartitionContent.Foreign ? Reason.Of(ReasonCode.UnreadablePartition) : null,
            ActionKind.ShrinkVolume => CheckResize(partition) ?? CheckShrinkMinimum(partition),
            ActionKind.ExtendVolume => CheckResize(partition) ?? CheckExtendSpace(disk, partition),
            ActionKind.ChangeDriveLetter => CheckNeedsReadableVolume(content)
                ?? (partition.DriveLetter is null && !facts.FreeDriveLetters.Any() ? Reason.Of(ReasonCode.AllDriveLettersInUse) : null),
            ActionKind.ChangeLabel => CheckNeedsReadableVolume(content),
            _ => throw new ArgumentException($"{kind} doesn't apply to a partition.", nameof(kind)),
        };
    }

    private static Reason? CheckNeedsReadableVolume(PartitionContent content) => content switch
    {
        PartitionContent.Foreign => Reason.Of(ReasonCode.UnreadablePartition),
        PartitionContent.Unformatted => Reason.Of(ReasonCode.NeedsFormatFirst),
        _ => null,
    };

    private static Reason? CheckResize(Partition partition) => partition.Content switch
    {
        PartitionContent.Foreign => Reason.Of(ReasonCode.UnreadablePartition),
        PartitionContent.Unformatted => Reason.Of(ReasonCode.ResizeNeedsFormat),
        _ when partition.Volume is { IsResizable: false } v =>
            Reason.Of(ReasonCode.ResizeUnsupportedFileSystem, Texts.FileSystem(v.FileSystem, v.FileSystemName)),
        _ => null,
    };

    private static Reason? CheckShrinkMinimum(Partition partition) =>
        partition.MinimumSize is ulong min && min + Layout.LayoutBuilder.MinimumUnallocatedSize >= partition.Size
            ? Reason.Of(ReasonCode.ShrinkAtMinimum)
            : null;

    private static Reason? CheckExtendSpace(Disk disk, Partition partition)
    {
        var layout = disk.Layout;
        var index = layout.IndexOf(layout.First(s => s is PartitionSegment ps && ps.Partition.Id == partition.Id));
        var next = index + 1 < layout.Length ? layout[index + 1] : null;

        if (next is UnallocatedSegment)
        {
            return null;
        }

        var name = DisplayNames.ForPartitionAsSubject(partition);
        var freeAfter = layout.Skip(index + 1).OfType<UnallocatedSegment>().Any();
        var freeBefore = layout.Take(index).OfType<UnallocatedSegment>().Any();

        if (freeAfter && next is PartitionSegment blocker)
        {
            return Reason.Of(ReasonCode.ExtendBlockedByPartition, name, DisplayNames.ForPartitionInSentence(blocker.Partition));
        }

        return freeBefore
            ? Reason.Of(ReasonCode.ExtendFreeSpaceOnlyBefore)
            : Reason.Of(ReasonCode.ExtendNoFreeSpace, name);
    }
}
