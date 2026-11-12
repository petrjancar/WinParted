using System.Collections.Immutable;
using WinParted.Core.Model;

namespace WinParted.Core.Tests.Builders;

/// <summary>Builds disks partition by partition, placing each one right after the previous.</summary>
public sealed class DiskBuilder
{
    public const ulong MB = 1024UL * 1024;
    public const ulong GB = 1024 * MB;
    public const ulong TB = 1024 * GB;

    private readonly List<Partition> _partitions = [];
    private ulong _cursor = MB;

    public DiskBuilder(int number, PartitionStyle style = PartitionStyle.Gpt)
    {
        Number = number;
        Style = style;
        Id = new DiskId($"disk-{number}-{Guid.NewGuid():N}");
    }

    public DiskId Id { get; }

    public int Number { get; }

    public PartitionStyle Style { get; }

    public string FriendlyName { get; set; } = "Test Disk";

    public BusType Bus { get; set; } = BusType.Sata;

    public bool IsSystem { get; set; }

    public bool IsBoot { get; set; }

    public bool IsOffline { get; set; }

    public bool IsReadOnly { get; set; }

    public bool IsRemovableMedia { get; set; }

    public bool? IsStorageSpacesMember { get; set; } = false;

    public HealthStatus Health { get; set; } = HealthStatus.Healthy;

    /// <summary>Adds a partition with no volume.</summary>
    public DiskBuilder Part(ulong size, Func<Partition, Partition>? configure = null)
    {
        var partition = new Partition
        {
            Id = new PartitionId(Id, _cursor),
            Number = _partitions.Count + 1,
            Size = size,
            GptType = Style == PartitionStyle.Gpt ? KnownPartitionTypes.GptBasicData : null,
            UniqueGuid = Style == PartitionStyle.Gpt ? Guid.NewGuid() : null,
            MbrType = Style == PartitionStyle.Mbr ? (byte)0x07 : null,
        };
        _partitions.Add(configure is null ? partition : configure(partition));
        _cursor += size;
        return this;
    }

    /// <summary>Adds a data partition holding a volume.</summary>
    public DiskBuilder Vol(
        ulong size,
        string? label = null,
        char? letter = null,
        FileSystemKind fs = FileSystemKind.Ntfs,
        ulong used = 0,
        Func<Volume, Volume>? volume = null,
        Func<Partition, Partition>? partition = null)
    {
        return Part(size, p =>
        {
            var v = new Volume
            {
                Id = new VolumeId($@"\\?\Volume{{{Guid.NewGuid()}}}\"),
                Label = label,
                FileSystem = fs,
                Size = size,
                SizeRemaining = size - used,
            };
            p = p with
            {
                DriveLetter = letter,
                AccessPaths = letter is char l ? [$"{l}:\\"] : ImmutableArray<string>.Empty,
                Volume = volume is null ? v : volume(v),
            };
            return partition is null ? p : partition(p);
        });
    }

    public DiskBuilder Free(ulong size)
    {
        _cursor += size;
        return this;
    }

    public Disk Build(ulong? totalSize = null) => new()
    {
        Id = Id,
        Number = Number,
        FriendlyName = FriendlyName,
        Size = totalSize ?? _cursor + MB,
        Bus = Bus,
        Style = Style,
        IsSystem = IsSystem,
        IsBoot = IsBoot,
        IsOffline = IsOffline,
        IsReadOnly = IsReadOnly,
        IsRemovableMedia = IsRemovableMedia,
        IsStorageSpacesMember = IsStorageSpacesMember,
        Health = Health,
        Partitions = [.. _partitions],
    };
}
