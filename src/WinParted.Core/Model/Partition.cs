using System.Collections.Immutable;

namespace WinParted.Core.Model;

public sealed record Partition
{
    public required PartitionId Id { get; init; }

    public ulong Offset => Id.Offset;

    public int Number { get; init; }

    public ulong Size { get; init; }

    public ulong End => Offset + Size;

    public Guid? GptType { get; init; }

    public byte? MbrType { get; init; }

    /// <summary>GPT partition GUID (unique per partition); null on MBR disks.</summary>
    public Guid? UniqueGuid { get; init; }

    /// <summary>SMP <c>IsSystem</c>: the partition the computer starts from (EFI or "System Reserved").</summary>
    public bool IsSystem { get; init; }

    /// <summary>SMP <c>IsBoot</c>: the partition holding the running Windows installation.</summary>
    public bool IsBoot { get; init; }

    public bool IsActive { get; init; }

    public bool IsHidden { get; init; }

    public bool IsReadOnly { get; init; }

    public char? DriveLetter { get; init; }

    /// <summary>All access paths (drive letter roots, mount folders and the volume GUID path).</summary>
    public ImmutableArray<string> AccessPaths { get; init; } = [];

    public Volume? Volume { get; init; }

    /// <summary>
    /// Smallest size Windows can shrink this partition to (SMP <c>GetSupportedSize</c>), or null
    /// when not queried yet.
    /// </summary>
    public ulong? MinimumSize { get; init; }

    public PartitionContent Content
    {
        get
        {
            if (KnownPartitionTypes.IsReserved(this))
            {
                return PartitionContent.Reserved;
            }

            if (Volume is { IsReadable: true })
            {
                return PartitionContent.Volume;
            }

            return KnownPartitionTypes.IsBasicData(this) && Volume is null or { FileSystem: FileSystemKind.None }
                ? PartitionContent.Unformatted
                : PartitionContent.Foreign;
        }
    }
}
