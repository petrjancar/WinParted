using WinParted.Core.Model;

namespace WinParted.Core.Operations;

/// <param name="Percent">0-100 when Windows reports progress; null for indeterminate.</param>
public sealed record OperationProgress(double? Percent);

/// <summary>Access to the storage backend (implemented with SMP in WinParted.Storage).</summary>
public interface IStorageProvider
{
    /// <summary>Reads a complete, fresh snapshot of all disks.</summary>
    Task<StorageSnapshot> ReadSnapshotAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes the operation. Throws <see cref="StorageOperationException"/> when Windows reports
    /// a failure. Operations can't be cancelled once started, so no cancellation token is taken.
    /// </summary>
    Task ExecuteAsync(OperationRequest request, IProgress<OperationProgress>? progress);

    /// <summary>
    /// The size range Windows allows for resizing this partition. Can take tens of seconds on a
    /// large system volume, so it is only called when the Shrink or Extend dialog opens.
    /// </summary>
    Task<SupportedSizeRange> GetSupportedSizeAsync(PartitionId partition, CancellationToken cancellationToken = default);

    /// <summary>Allocation unit sizes Windows offers for formatting this partition with the file system.</summary>
    Task<IReadOnlyList<uint>> GetSupportedClusterSizesAsync(PartitionId partition, FileSystemKind fileSystem, CancellationToken cancellationToken = default);
}

public sealed record SupportedSizeRange(ulong Minimum, ulong Maximum);

/// <summary>A failure reported by the storage backend.</summary>
/// <param name="Message">Plain-language explanation of what happened and what the user can do.</param>
/// <param name="MayHaveChanged">False only when it's certain nothing on disk was modified.</param>
/// <param name="Code">The backend's numeric status code, if any.</param>
/// <param name="Details">Raw technical details for the "Technical details" section.</param>
public sealed record StorageError(string Message, bool MayHaveChanged, uint? Code = null, string? Details = null);

public sealed class StorageOperationException(StorageError error, Exception? inner = null)
    : Exception(error.Message, inner)
{
    public StorageError Error { get; } = error;
}
