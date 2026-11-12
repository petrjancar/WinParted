using WinParted.Core.Model;
using WinParted.Core.Operations;

namespace WinParted.Core.Samples;

/// <summary>
/// The design brief's sample disks (mvp-features.md section 10) plus a BitLocker disk, for running
/// the app with <c>--sample</c>: every state can be reviewed without touching real disks. Changes
/// are never applied.
/// </summary>
public sealed class SampleStorageProvider : IStorageProvider
{
    private const ulong MB = 1024UL * 1024;
    private const ulong GB = 1024 * MB;

    public static readonly Guid GptLinuxData = new("0fc63daf-8483-4772-8e79-3d69d8477de4");

    public Task<StorageSnapshot> ReadSnapshotAsync(CancellationToken cancellationToken = default) => Task.FromResult(Snapshot());

    public async Task ExecuteAsync(OperationRequest request, IProgress<OperationProgress>? progress)
    {
        for (var p = 0; p <= 100; p += 10)
        {
            progress?.Report(new OperationProgress(p));
            await Task.Delay(150).ConfigureAwait(false);
        }

        throw new StorageOperationException(new StorageError(
            "Sample mode shows example disks only. Nothing was changed.", MayHaveChanged: false, Details: $"{request.Kind} in sample mode"));
    }

    public Task<SupportedSizeRange> GetSupportedSizeAsync(PartitionId partition, CancellationToken cancellationToken = default) =>
        Task.Delay(1500, cancellationToken).ContinueWith(_ => new SupportedSizeRange(520 * GB, 930 * GB), cancellationToken, TaskContinuationOptions.None, TaskScheduler.Default);

    public Task<IReadOnlyList<uint>> GetSupportedClusterSizesAsync(PartitionId partition, FileSystemKind fileSystem, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<uint>>([4096, 8192, 16384, 32768, 65536]);

    public static StorageSnapshot Snapshot()
    {
        var disks = new[]
        {
            new Builder(0, PartitionStyle.Gpt, "Samsung SSD 980 PRO 1TB", BusType.Nvme) { System = true, Serial = "S5GXNX0T412K", Location = "PCIe slot 0 (NVMe)" }
                .Vol(100 * MB, fs: FileSystemKind.Fat32, used: 30 * MB, gpt: KnownPartitionTypes.GptEfiSystem, isSystem: true)
                .Part(16 * MB, KnownPartitionTypes.GptMicrosoftReserved)
                .Vol(830 * GB, label: "Windows", letter: 'C', used: 412 * GB, isBoot: true, windows: true, pagefile: true)
                .Vol(750 * MB, used: 500 * MB, gpt: KnownPartitionTypes.GptRecovery, hidden: true)
                .Free(100 * GB)
                .Build(),
            new Builder(1, PartitionStyle.Gpt, "WDC WD20EZAZ", BusType.Sata) { Serial = "WD-WX12D80KH3V7", Location = "SATA port 1" }
                .Vol(1198 * GB, label: "Data", letter: 'D', used: 740 * GB)
                .Part(300 * GB, GptLinuxData)
                .Free(365 * GB)
                .Build(),
            new Builder(2, PartitionStyle.Mbr, "SanDisk Ultra USB 3.0", BusType.Usb) { Serial = "4C530001230412117423", Location = "USB port", Removable = true }
                .Vol((ulong)(57.3 * GB) - MB, label: "BACKUP", letter: 'E', fs: FileSystemKind.ExFat, used: 41 * GB)
                .Build(),
            new Builder(3, PartitionStyle.Uninitialized, "Generic SD/MMC", BusType.Sd) { Location = "SD card reader" }
                .Build(totalSize: (ulong)(29.7 * GB)),
            new Builder(4, PartitionStyle.Gpt, "Msft Virtual Disk", BusType.Virtual) { Offline = "Policy", Location = @"C:\VMs\scratch.vhdx" }
                .Vol(60 * GB, label: "Scratch", used: 12 * GB)
                .Build(totalSize: 64 * GB),
            new Builder(5, PartitionStyle.Gpt, "Crucial MX500 500GB", BusType.Sata) { Serial = "2233E6A1B2C3" }
                .Vol(200 * GB, label: "Vault", letter: 'F', used: 80 * GB, bitLocker: BitLockerState.Locked)
                .Vol(265 * GB, label: "Photos", letter: 'G', used: 90 * GB, bitLocker: BitLockerState.Unlocked)
                .Build(),
        };

        return new StorageSnapshot
        {
            Disks = [.. disks],
            Facts = new SystemFacts { UsedDriveLetters = ['C', 'D', 'E', 'F', 'G'] },
        };
    }

    private sealed class Builder(int number, PartitionStyle style, string name, BusType bus)
    {
        private readonly DiskId _id = new($"sample-disk-{number}");
        private readonly List<Partition> _partitions = [];
        private ulong _cursor = MB;

        public bool System { get; init; }

        public string? Serial { get; init; }

        public string? Location { get; init; }

        public string? Offline { get; init; }

        public bool Removable { get; init; }

        public Builder Part(ulong size, Guid gptType)
        {
            _partitions.Add(new Partition
            {
                Id = new PartitionId(_id, _cursor),
                Number = _partitions.Count + 1,
                Size = size,
                GptType = gptType,
                IsHidden = gptType == KnownPartitionTypes.GptMicrosoftReserved,
            });
            _cursor += size;
            return this;
        }

        public Builder Vol(
            ulong size,
            string? label = null,
            char? letter = null,
            FileSystemKind fs = FileSystemKind.Ntfs,
            ulong used = 0,
            Guid? gpt = null,
            bool isSystem = false,
            bool isBoot = false,
            bool windows = false,
            bool pagefile = false,
            bool hidden = false,
            BitLockerState bitLocker = BitLockerState.NotEncrypted)
        {
            _partitions.Add(new Partition
            {
                Id = new PartitionId(_id, _cursor),
                Number = _partitions.Count + 1,
                Size = size,
                GptType = style == PartitionStyle.Gpt ? gpt ?? KnownPartitionTypes.GptBasicData : null,
                MbrType = style == PartitionStyle.Mbr ? (byte)0x07 : null,
                IsSystem = isSystem,
                IsBoot = isBoot,
                IsHidden = hidden,
                DriveLetter = letter,
                AccessPaths = letter is char l ? [$"{l}:\\"] : [],
                Volume = new Volume
                {
                    Id = new VolumeId($"sample-volume-{number}-{_cursor}"),
                    Label = label,
                    FileSystem = fs,
                    Size = size,
                    SizeRemaining = size - used,
                    IsWindowsVolume = windows,
                    HasPagefileOrDump = pagefile,
                    BitLocker = bitLocker,
                },
            });
            _cursor += size;
            return this;
        }

        public Builder Free(ulong size)
        {
            _cursor += size;
            return this;
        }

        public Disk Build(ulong? totalSize = null) => new()
        {
            Id = _id,
            Number = number,
            FriendlyName = name,
            SerialNumber = Serial,
            Location = Location,
            Size = totalSize ?? _cursor + MB,
            Bus = bus,
            Style = style,
            IsSystem = System,
            IsBoot = System,
            IsOffline = Offline is not null,
            IsRemovableMedia = Removable,
            OfflineReason = Offline,
            Partitions = [.. _partitions],
        };
    }
}
