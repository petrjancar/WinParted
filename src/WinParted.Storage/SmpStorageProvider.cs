using Microsoft.Management.Infrastructure;
using WinParted.Core.Actions;
using WinParted.Core.Model;
using WinParted.Core.Operations;
using WinParted.Storage.Cim;

namespace WinParted.Storage;

/// <summary>
/// <see cref="IStorageProvider"/> on the Windows Storage Management API (design D2, D8). It performs
/// no safety checks of its own: <see cref="OperationRunner"/> checks availability, identity and
/// fingerprint right before calling <see cref="ExecuteAsync"/>.
/// </summary>
public sealed class SmpStorageProvider : IStorageProvider, IDisposable
{
    private readonly CimSession _session;
    private readonly Smp _smp;

    public SmpStorageProvider()
    {
        _session = CimSession.Create(null);
        _smp = new Smp(_session);
    }

    public Task<StorageSnapshot> ReadSnapshotAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => SnapshotReader.Read(_smp), cancellationToken);

    public Task ExecuteAsync(OperationRequest request, IProgress<OperationProgress>? progress) =>
        Task.Run(() => Execute(request, progress));

    public Task<SupportedSizeRange> GetSupportedSizeAsync(PartitionId partition, CancellationToken cancellationToken = default) =>
        Task.Run(
            () =>
            {
                var r = _smp.Invoke(FindPartition(partition), "GetSupportedSize");
                return new SupportedSizeRange(
                    Convert.ToUInt64(r.OutParameters["SizeMin"]?.Value ?? 0UL),
                    Convert.ToUInt64(r.OutParameters["SizeMax"]?.Value ?? 0UL));
            },
            cancellationToken);

    public Task<IReadOnlyList<uint>> GetSupportedClusterSizesAsync(PartitionId partition, FileSystemKind fileSystem, CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<uint>>(
            () =>
            {
                var volume = FindVolume(FindPartition(partition));
                var r = _smp.Invoke(volume, "GetSupportedClusterSizes", ("FileSystem", FileSystemName(fileSystem), CimType.String));
                return r.OutParameters["SupportedClusterSizes"]?.Value is uint[] sizes ? sizes : [];
            },
            cancellationToken);

    public void Dispose() => _session.Dispose();

    private void Execute(OperationRequest request, IProgress<OperationProgress>? progress)
    {
        switch (request)
        {
            case InitializeDiskRequest r:
                _smp.Invoke(FindDisk(r.Disk.Disk), "Initialize",
                    ("PartitionStyle", r.Style == PartitionStyle.Gpt ? (ushort)2 : (ushort)1, CimType.UInt16));
                break;

            case SetDiskOnlineRequest r:
                _smp.Invoke(FindDisk(r.Disk.Disk), r.Online ? "Online" : "Offline");
                break;

            case ClearDiskRequest r:
                _smp.Invoke(FindDisk(r.Disk.Disk), "Clear",
                    ("RemoveData", true, CimType.Boolean),
                    ("RemoveOEM", true, CimType.Boolean),
                    ("ZeroOutEntireDisk", false, CimType.Boolean));
                break;

            case CreatePartitionRequest r:
                CreatePartition(r, progress);
                break;

            case DeletePartitionRequest r:
                _smp.Invoke(FindPartition(r.Partition.Partition), "DeleteObject");
                break;

            case FormatVolumeRequest r:
                Format(FindVolume(FindPartition(r.Partition.Partition)), r.Options, progress);
                break;

            case ResizePartitionRequest r:
                _smp.Invoke(FindPartition(r.Partition.Partition), "Resize", ("Size", r.NewSize, CimType.UInt64));
                break;

            case SetDriveLetterRequest r:
                SetDriveLetter(r.Partition.Partition, r.Letter);
                break;

            case SetLabelRequest r:
                _smp.Invoke(FindVolume(FindPartition(r.Partition.Partition)), "SetFileSystemLabel",
                    ("FileSystemLabel", r.Label, CimType.String));
                break;

            default:
                throw new NotSupportedException($"Unknown operation {request.GetType().Name}.");
        }
    }

    private void CreatePartition(CreatePartitionRequest r, IProgress<OperationProgress>? progress)
    {
        var result = _smp.Invoke(FindDisk(r.Region.Disk), "CreatePartition",
            ("Size", r.Size, CimType.UInt64),
            ("Offset", r.Region.Offset, CimType.UInt64),
            ("AssignDriveLetter", false, CimType.Boolean));

        var offset = result.OutParameters["CreatedPartition"]?.Value is CimInstance created
            ? Smp.U64(created, "Offset")
            : r.Region.Offset;
        var id = new PartitionId(r.Region.Disk, offset);

        if (r.Format is { } format)
        {
            try
            {
                Format(WaitForVolume(id), format, progress);
            }
            catch (StorageOperationException ex)
            {
                throw new StorageOperationException(
                    StorageErrorMapper.Create(StorageErrorKind.CreatedButFormatFailed, ex.Error.Code, "Format after CreatePartition", ex.Error.Details),
                    ex);
            }
        }

        // Windows sometimes assigns a letter on its own (spike 2.1); enforce the requested one.
        SetDriveLetter(id, r.Format is null ? null : r.DriveLetter);
    }

    private void Format(CimInstance volume, FormatOptions options, IProgress<OperationProgress>? progress)
    {
        var args = new List<(string, object, CimType)>
        {
            ("FileSystem", FileSystemName(options.FileSystem), CimType.String),
            ("Full", !options.Quick, CimType.Boolean),
        };
        if (options.Label is not null)
        {
            args.Add(("FileSystemLabel", options.Label, CimType.String));
        }

        if (options.AllocationUnitSize is uint unit)
        {
            args.Add(("AllocationUnitSize", unit, CimType.UInt32));
        }

        _smp.InvokeWithProgress(volume, "Format", progress, [.. args]);
    }

    private void SetDriveLetter(PartitionId id, char? letter)
    {
        var partition = FindPartition(id);
        var current = Smp.Letter(partition);
        if (current == letter)
        {
            return;
        }

        if (current is char old)
        {
            _smp.Invoke(partition, "RemoveAccessPath", ("AccessPath", $"{old}:\\", CimType.String));
        }

        if (letter is char wanted)
        {
            try
            {
                _smp.Invoke(FindPartition(id), "AddAccessPath", ("AccessPath", $"{wanted}:\\", CimType.String));
            }
            catch (StorageOperationException ex) when (current is not null)
            {
                // The old letter is already gone, so something did change.
                throw new StorageOperationException(ex.Error with { MayHaveChanged = true }, ex);
            }
        }
    }

    private CimInstance FindDisk(DiskId id) =>
        _smp.Query("MSFT_Disk").FirstOrDefault(d => Smp.Str(d, "ObjectId") == id.Value) ?? throw TargetGone();

    private CimInstance FindPartition(PartitionId id)
    {
        var diskPath = Smp.Str(FindDisk(id.Disk), "Path");
        return _smp.Query("MSFT_Partition").FirstOrDefault(p =>
                string.Equals(Smp.Str(p, "DiskId"), diskPath, StringComparison.OrdinalIgnoreCase) && Smp.U64(p, "Offset") == id.Offset)
            ?? throw TargetGone();
    }

    private CimInstance FindVolume(CimInstance partition)
    {
        var paths = (Smp.Prop(partition, "AccessPaths") as string[] ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return _smp.Query("MSFT_Volume").FirstOrDefault(v => Smp.Str(v, "Path") is { } p && paths.Contains(p)) ?? throw TargetGone();
    }

    /// <summary>A freshly created partition's volume can take a moment to appear.</summary>
    private CimInstance WaitForVolume(PartitionId id)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return FindVolume(FindPartition(id));
            }
            catch (StorageOperationException) when (attempt < 20)
            {
                Thread.Sleep(250);
            }
        }
    }

    private static string FileSystemName(FileSystemKind fs) => fs switch
    {
        FileSystemKind.Ntfs => "NTFS",
        FileSystemKind.ExFat => "exFAT",
        FileSystemKind.Fat32 => "FAT32",
        FileSystemKind.Fat => "FAT",
        FileSystemKind.ReFS => "ReFS",
        _ => throw new ArgumentOutOfRangeException(nameof(fs), fs, "Not a format target."),
    };

    private static StorageOperationException TargetGone() =>
        new(new StorageError(Reason.Of(ReasonCode.TargetChanged).Text, MayHaveChanged: false, Details: "Target not found in SMP"));
}
