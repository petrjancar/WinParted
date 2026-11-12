using WinParted.Core.Model;

namespace WinParted.Core.Selections;

/// <summary>A reference to one layout segment that survives snapshot refreshes.</summary>
public abstract record SegmentRef;

public sealed record PartitionRef(PartitionId Id) : SegmentRef;

public sealed record UnallocatedRef(DiskId Disk, ulong Offset) : SegmentRef;

/// <summary>The selected disk and, optionally, one segment on it.</summary>
public sealed record Selection(DiskId? Disk, SegmentRef? Segment)
{
    public static Selection None { get; } = new(null, null);

    public static Selection OfDisk(DiskId disk) => new(disk, null);

    public static Selection OfPartition(PartitionId id) => new(id.Disk, new PartitionRef(id));

    public static Selection OfUnallocated(DiskId disk, ulong offset) => new(disk, new UnallocatedRef(disk, offset));
}

public sealed record SelectionReconcileResult(Selection Selection, bool SelectedDiskRemoved);

public static class SelectionReconciler
{
    /// <summary>
    /// Maps a selection onto a new snapshot (spec: disk-inventory "Manual refresh",
    /// device-change-detection "Selected disk removed"):
    /// the same item stays selected if it still exists; a vanished segment falls back to its disk;
    /// a vanished disk clears the selection.
    /// </summary>
    public static SelectionReconcileResult Reconcile(Selection previous, StorageSnapshot snapshot)
    {
        if (previous.Disk is not DiskId diskId)
        {
            return new(Selection.None, false);
        }

        var disk = snapshot.FindDisk(diskId);
        if (disk is null)
        {
            return new(Selection.None, true);
        }

        var selection = previous.Segment switch
        {
            null => Selection.OfDisk(diskId),
            PartitionRef p => snapshot.FindPartition(p.Id) is not null ? previous : Selection.OfDisk(diskId),
            UnallocatedRef u => disk.UnallocatedSegments.FirstOrDefault(s => s.Offset <= u.Offset && u.Offset < s.End)
                is UnallocatedSegment match
                ? Selection.OfUnallocated(diskId, match.Offset)
                : Selection.OfDisk(diskId),
            _ => Selection.OfDisk(diskId),
        };

        return new(selection, false);
    }
}
