using System.Collections.Immutable;
using WinParted.Core.Model;

namespace WinParted.Core.Layout;

public static class LayoutBuilder
{
    /// <summary>
    /// Gaps of 1 MB or less come from partition alignment and the partition table itself, so they
    /// are not shown as unallocated space (spec: disk-inventory, "Partition layout bar").
    /// </summary>
    public const ulong MinimumUnallocatedSize = 1024 * 1024;

    /// <summary>Windows aligns the first partition to 1 MB.</summary>
    public const ulong FirstUsableOffset = 1024 * 1024;

    private const ulong GptBackupSectors = 33;

    public static ImmutableArray<LayoutSegment> Build(Disk disk)
    {
        if (disk.Style == PartitionStyle.Uninitialized || disk.IsOffline)
        {
            return [];
        }

        var segments = ImmutableArray.CreateBuilder<LayoutSegment>();

        // The first 1 MB holds the partition table and alignment padding; Windows never places a new
        // partition before it. GPT disks also keep a backup header in their last 33 sectors.
        ulong cursor = FirstUsableOffset;
        var usableEnd = disk.Style == PartitionStyle.Gpt
            ? disk.Size - Math.Min(disk.Size, GptBackupSectors * disk.LogicalSectorSize)
            : disk.Size;

        // MBR extended containers are skipped: their logical partitions are listed individually, and
        // the gaps of about 64 KB between logical partitions fall under the 1 MB threshold.
        // Confirmed by spike 2.3 (docs/spikes/smp-behavior.md).
        foreach (var partition in disk.Partitions.Where(p => !KnownPartitionTypes.IsMbrExtendedContainer(p)))
        {
            AddGap(disk, segments, cursor, partition.Offset);
            segments.Add(new PartitionSegment(partition));
            cursor = Math.Max(cursor, partition.End);
        }

        AddGap(disk, segments, cursor, usableEnd);
        return segments.ToImmutable();
    }

    private static void AddGap(Disk disk, ImmutableArray<LayoutSegment>.Builder segments, ulong start, ulong end)
    {
        if (end > start && end - start > MinimumUnallocatedSize)
        {
            segments.Add(new UnallocatedSegment(disk.Id, start, end - start));
        }
    }
}
