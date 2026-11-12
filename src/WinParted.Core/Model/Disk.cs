using System.Collections.Immutable;
using WinParted.Core.Layout;

namespace WinParted.Core.Model;

public sealed class Disk
{
    private readonly ImmutableArray<Partition> _partitions = [];
    private ImmutableArray<LayoutSegment>? _layout;

    public required DiskId Id { get; init; }

    /// <summary>Windows disk number. Display only; see <see cref="DiskId"/>.</summary>
    public int Number { get; init; }

    public string FriendlyName { get; init; } = string.Empty;

    public string? SerialNumber { get; init; }

    public string? Location { get; init; }

    public ulong Size { get; init; }

    public uint LogicalSectorSize { get; init; } = 512;

    public BusType Bus { get; init; }

    public PartitionStyle Style { get; init; }

    public bool IsOffline { get; init; }

    /// <summary>Windows' reason for the disk being offline (e.g. "Policy"), if any.</summary>
    public string? OfflineReason { get; init; }

    public HealthStatus Health { get; init; } = HealthStatus.Healthy;

    public bool IsReadOnly { get; init; }

    /// <summary>Removable media such as USB flash drives and SD cards. Windows can't take these offline.</summary>
    public bool IsRemovableMedia { get; init; }

    /// <summary>SMP <c>MSFT_Disk.IsSystem</c>: the disk the computer starts from.</summary>
    public bool IsSystem { get; init; }

    /// <summary>SMP <c>MSFT_Disk.IsBoot</c>: the disk holding the running Windows installation.</summary>
    public bool IsBoot { get; init; }

    /// <summary>
    /// True when the disk belongs to a Storage Spaces pool. Null means it could not be determined,
    /// which is treated as protected (fail closed).
    /// </summary>
    public bool? IsStorageSpacesMember { get; init; } = false;

    /// <summary>Partitions ordered by offset.</summary>
    public ImmutableArray<Partition> Partitions
    {
        get => _partitions;
        init => _partitions = [.. value.OrderBy(p => p.Offset)];
    }

    /// <summary>Partitions and unallocated regions in on-disk order, as shown in the layout bar.</summary>
    public ImmutableArray<LayoutSegment> Layout => _layout ??= LayoutBuilder.Build(this);

    public bool IsDynamic => Partitions.Any(KnownPartitionTypes.IsLdm);

    public IEnumerable<UnallocatedSegment> UnallocatedSegments => Layout.OfType<UnallocatedSegment>();

    public ulong UnallocatedSize => UnallocatedSegments.Aggregate(0UL, (sum, s) => sum + s.Size);
}
