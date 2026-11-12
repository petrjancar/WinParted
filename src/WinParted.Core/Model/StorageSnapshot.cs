using System.Collections.Immutable;

namespace WinParted.Core.Model;

/// <summary>Facts about the running system that are not part of any single disk.</summary>
public sealed record SystemFacts
{
    /// <summary>Drive letters currently in use (including network and optical drives).</summary>
    public ImmutableHashSet<char> UsedDriveLetters { get; init; } = [];

    /// <summary>Letters a volume may be given: C to Z, minus those in use.</summary>
    public IEnumerable<char> FreeDriveLetters =>
        Enumerable.Range('C', 'Z' - 'C' + 1).Select(c => (char)c).Where(c => !UsedDriveLetters.Contains(c));
}

/// <summary>
/// An immutable, complete picture of all disks at one moment. Every refresh reads a new snapshot;
/// nothing is patched incrementally (design D3).
/// </summary>
public sealed class StorageSnapshot
{
    private readonly ImmutableArray<Disk> _disks = [];

    public static StorageSnapshot Empty { get; } = new();

    /// <summary>Disks ordered by disk number.</summary>
    public ImmutableArray<Disk> Disks
    {
        get => _disks;
        init => _disks = [.. value.OrderBy(d => d.Number)];
    }

    public SystemFacts Facts { get; init; } = new();

    public Disk? FindDisk(DiskId id) => Disks.FirstOrDefault(d => d.Id == id);

    public Partition? FindPartition(PartitionId id) =>
        FindDisk(id.Disk)?.Partitions.FirstOrDefault(p => p.Id == id);
}
