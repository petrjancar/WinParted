namespace WinParted.Core.Model;

/// <summary>One segment of a disk's layout bar: a partition or an unallocated region.</summary>
public abstract record LayoutSegment(DiskId Disk, ulong Offset, ulong Size)
{
    public ulong End => Offset + Size;
}

public sealed record PartitionSegment(Partition Partition)
    : LayoutSegment(Partition.Id.Disk, Partition.Offset, Partition.Size);

public sealed record UnallocatedSegment(DiskId Disk, ulong Offset, ulong Size)
    : LayoutSegment(Disk, Offset, Size);
