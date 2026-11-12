using WinParted.Core.Model;
using static WinParted.Core.Tests.Builders.DiskBuilder;

namespace WinParted.Core.Tests.Builders;

/// <summary>Typical real-world disk layouts used across tests (task 4.4).</summary>
public static class Layouts
{
    public static readonly Guid GptLinuxData = new("0fc63daf-8483-4772-8e79-3d69d8477de4");

    /// <summary>
    /// Windows 11 default install: EFI, MSR, C: (Windows + pagefile), Recovery, then 100 GB free.
    /// Extend on C: is blocked by the Recovery partition.
    /// </summary>
    public static Disk Windows11Default(int number = 0) =>
        new DiskBuilder(number)
        {
            FriendlyName = "Samsung SSD 980 PRO 1TB",
            Bus = BusType.Nvme,
            IsSystem = true,
            IsBoot = true,
        }
        .Vol(100 * MB, fs: FileSystemKind.Fat32, partition: p => p with
        {
            GptType = KnownPartitionTypes.GptEfiSystem,
            IsSystem = true,
        })
        .Part(16 * MB, p => p with { GptType = KnownPartitionTypes.GptMicrosoftReserved })
        .Vol(830 * GB, letter: 'C', used: 412 * GB,
            volume: v => v with { IsWindowsVolume = true, HasPagefileOrDump = true },
            partition: p => p with { IsBoot = true })
        .Vol(750 * MB, used: 500 * MB, partition: p => p with { GptType = KnownPartitionTypes.GptRecovery })
        .Free(100 * GB)
        .Build();

    /// <summary>Legacy BIOS install: "System Reserved" (active), C: (Windows), D: data.</summary>
    public static Disk LegacyMbr(int number = 0) =>
        new DiskBuilder(number, PartitionStyle.Mbr)
        {
            FriendlyName = "WDC WD5000AAKX",
            IsSystem = true,
            IsBoot = true,
        }
        .Vol(500 * MB, label: "System Reserved", used: 30 * MB,
            partition: p => p with { IsSystem = true, IsActive = true })
        .Vol(200 * GB, letter: 'C', used: 80 * GB,
            volume: v => v with { IsWindowsVolume = true, HasPagefileOrDump = true },
            partition: p => p with { IsBoot = true })
        .Vol(265 * GB, label: "Data", letter: 'D', used: 100 * GB)
        .Build();

    /// <summary>Second internal disk: Data (D:), a Linux partition, then 320 GB free.</summary>
    public static Disk DataDiskWithLinux(int number = 1) =>
        new DiskBuilder(number) { FriendlyName = "WDC WD20EZAZ" }
        .Vol(1200 * GB, label: "Data", letter: 'D', used: 740 * GB)
        .Part(300 * GB, p => p with { GptType = GptLinuxData })
        .Free(320 * GB)
        .Build();

    /// <summary>USB stick with one exFAT volume, BACKUP (E:).</summary>
    public static Disk UsbStick(int number = 2) =>
        new DiskBuilder(number, PartitionStyle.Mbr) { FriendlyName = "SanDisk Ultra USB 3.0", Bus = BusType.Usb, IsRemovableMedia = true }
        .Vol(57 * GB, label: "BACKUP", letter: 'E', fs: FileSystemKind.ExFat, used: 41 * GB)
        .Build();

    /// <summary>A dynamic disk (LDM metadata + LDM data partitions).</summary>
    public static Disk DynamicDisk(int number = 3) =>
        new DiskBuilder(number) { FriendlyName = "Old RAID member" }
        .Part(1 * MB, p => p with { GptType = KnownPartitionTypes.GptLdmMetadata })
        .Part(16 * MB, p => p with { GptType = KnownPartitionTypes.GptMicrosoftReserved })
        .Part(500 * GB, p => p with { GptType = KnownPartitionTypes.GptLdmData })
        .Build();

    /// <summary>Disk with one BitLocker-encrypted volume F: in the given state.</summary>
    public static Disk BitLocker(BitLockerState state, int number = 4) =>
        new DiskBuilder(number) { FriendlyName = "Crucial MX500" }
        .Vol(465 * GB, label: "Secure", letter: 'F', used: 100 * GB, volume: v => v with { BitLocker = state })
        .Build();

    /// <summary>Brand-new SD card with no partition table.</summary>
    public static Disk Uninitialized(int number = 5) =>
        new DiskBuilder(number, PartitionStyle.Uninitialized) { FriendlyName = "Generic SD/MMC", Bus = BusType.Sd }
        .Build(totalSize: 29 * GB);

    public static StorageSnapshot Snapshot(params Disk[] disks) => new()
    {
        Disks = [.. disks],
        Facts = new SystemFacts
        {
            UsedDriveLetters = [.. disks.SelectMany(d => d.Partitions).Select(p => p.DriveLetter).OfType<char>()],
        },
    };
}
