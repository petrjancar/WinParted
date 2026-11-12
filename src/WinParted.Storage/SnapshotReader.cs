using System.Collections.Immutable;
using Microsoft.Management.Infrastructure;
using WinParted.Core.Model;
using WinParted.Storage.Cim;
using CoreFacts = WinParted.Core.Model.SystemFacts;

namespace WinParted.Storage;

/// <summary>Reads a complete <see cref="StorageSnapshot"/> from SMP (design D3, task 7.1).</summary>
internal static class SnapshotReader
{
    public static StorageSnapshot Read(Smp smp)
    {
        var disks = smp.Query("MSFT_Disk");
        var partitions = smp.Query("MSFT_Partition");
        var volumesByPath = smp.Query("MSFT_Volume")
            .Where(v => Smp.Str(v, "Path") is not null)
            .GroupBy(v => Smp.Str(v, "Path")!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var facts = SystemFacts.Read(smp);

        // MSFT_Partition.DiskId holds the disk's Path, not its ObjectId (spike 2.3).
        var partitionsByDiskPath = partitions
            .GroupBy(p => Smp.Str(p, "DiskId") ?? "", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        return new StorageSnapshot
        {
            Disks = [.. disks.Select(d => MapDisk(d, partitionsByDiskPath, volumesByPath, facts))],
            Facts = new CoreFacts
            {
                UsedDriveLetters = [.. facts.OtherDriveLetters
                    .Concat(partitions.Select(Smp.Letter).OfType<char>())],
            },
        };
    }

    private static Disk MapDisk(
        CimInstance d,
        Dictionary<string, List<CimInstance>> partitionsByDiskPath,
        Dictionary<string, CimInstance> volumesByPath,
        SystemFacts facts)
    {
        var id = new DiskId(Smp.Str(d, "ObjectId") ?? "");
        var number = Smp.Prop(d, "Number") is { } n ? Convert.ToUInt32(n) : uint.MaxValue;
        var parts = partitionsByDiskPath.TryGetValue(Smp.Str(d, "Path") ?? "", out var list) ? list : [];

        return new Disk
        {
            Id = id,
            Number = (int)number,
            FriendlyName = Smp.Str(d, "FriendlyName") ?? "",
            SerialNumber = Smp.Str(d, "SerialNumber")?.Trim(),
            Location = Smp.Str(d, "Location"),
            Size = Smp.U64(d, "Size"),
            LogicalSectorSize = Smp.Prop(d, "LogicalSectorSize") is { } s ? Convert.ToUInt32(s) : 512,
            Bus = MapBus(Smp.U16(d, "BusType")),
            Style = Smp.U16(d, "PartitionStyle") switch { 1 => PartitionStyle.Mbr, 2 => PartitionStyle.Gpt, _ => PartitionStyle.Uninitialized },
            IsOffline = Smp.Bool(d, "IsOffline"),
            OfflineReason = Smp.Bool(d, "IsOffline") ? OfflineReasonText(Smp.U16(d, "OfflineReason")) : null,
            Health = Smp.U16(d, "HealthStatus") switch { 0 => HealthStatus.Healthy, 1 => HealthStatus.Warning, 2 => HealthStatus.Unhealthy, _ => HealthStatus.Unknown },
            IsReadOnly = Smp.Bool(d, "IsReadOnly"),
            IsRemovableMedia = facts.RemovableDiskNumbers.Contains(number),
            IsSystem = Smp.Bool(d, "IsSystem"),
            IsBoot = Smp.Bool(d, "IsBoot"),
            IsStorageSpacesMember = facts.PooledByDiskNumber is null ? null : facts.PooledByDiskNumber.GetValueOrDefault(number),
            Partitions = [.. parts.Select(p => MapPartition(p, id, volumesByPath, facts))],
        };
    }

    private static Partition MapPartition(CimInstance p, DiskId disk, Dictionary<string, CimInstance> volumesByPath, SystemFacts facts)
    {
        var accessPaths = Smp.Prop(p, "AccessPaths") as string[] ?? [];
        var volume = accessPaths.Select(a => volumesByPath.GetValueOrDefault(a)).FirstOrDefault(v => v is not null);
        var letter = Smp.Letter(p);
        var mbrType = Smp.U16(p, "MbrType");

        return new Partition
        {
            Id = new PartitionId(disk, Smp.U64(p, "Offset")),
            Number = (int)(Smp.Prop(p, "PartitionNumber") is { } n ? Convert.ToUInt32(n) : 0),
            Size = Smp.U64(p, "Size"),
            GptType = Guid.TryParse(Smp.Str(p, "GptType"), out var gpt) ? gpt : null,
            MbrType = mbrType != 0 ? (byte)mbrType : null,
            UniqueGuid = Guid.TryParse(Smp.Str(p, "Guid"), out var guid) ? guid : null,
            IsSystem = Smp.Bool(p, "IsSystem"),
            IsBoot = Smp.Bool(p, "IsBoot"),
            IsActive = Smp.Bool(p, "IsActive"),
            IsHidden = Smp.Bool(p, "IsHidden"),
            IsReadOnly = Smp.Bool(p, "IsReadOnly"),
            DriveLetter = letter,
            AccessPaths = [.. accessPaths],
            Volume = volume is null ? null : MapVolume(volume, letter, facts),
        };
    }

    private static Volume MapVolume(CimInstance v, char? letter, SystemFacts facts)
    {
        var path = Smp.Str(v, "Path") ?? "";
        var fsName = Smp.Str(v, "FileSystem");
        return new Volume
        {
            Id = new VolumeId(Smp.Str(v, "UniqueId") ?? path),
            Label = Smp.Str(v, "FileSystemLabel") is { Length: > 0 } label ? label : null,
            FileSystem = MapFileSystem(fsName),
            FileSystemName = fsName,
            Size = Smp.U64(v, "Size"),
            SizeRemaining = Smp.U64(v, "SizeRemaining"),
            IsWindowsVolume = letter == facts.WindowsLetter,
            HasPagefileOrDump = facts.PagefileLetters is null ? null : letter is char l && facts.PagefileLetters.Contains(l),
            BitLocker = facts.BitLockerByVolumePath is null
                ? BitLockerState.Unknown
                : facts.BitLockerByVolumePath.GetValueOrDefault(path, BitLockerState.NotEncrypted),
        };
    }

    public static BusType MapBus(ushort busType) => busType switch
    {
        1 => BusType.Scsi,
        3 => BusType.Ata,
        7 => BusType.Usb,
        8 => BusType.Raid,
        10 => BusType.Sas,
        11 => BusType.Sata,
        12 => BusType.Sd,
        13 => BusType.Mmc,
        14 or 15 => BusType.Virtual,
        16 => BusType.StorageSpaces,
        17 => BusType.Nvme,
        0 => BusType.Unknown,
        _ => BusType.Other,
    };

    public static FileSystemKind MapFileSystem(string? name) => name?.ToUpperInvariant() switch
    {
        null or "" or "RAW" or "UNKNOWN" => FileSystemKind.None,
        "NTFS" => FileSystemKind.Ntfs,
        "REFS" => FileSystemKind.ReFS,
        "FAT" => FileSystemKind.Fat,
        "FAT32" => FileSystemKind.Fat32,
        "EXFAT" => FileSystemKind.ExFat,
        "UDF" => FileSystemKind.Udf,
        "CSVFS" => FileSystemKind.Csvfs,
        _ => FileSystemKind.Other,
    };

    private static string? OfflineReasonText(ushort reason) => reason switch
    {
        1 => "Policy",
        2 => "Redundant path",
        3 => "Snapshot",
        4 => "Signature collision",
        5 => "Resource exhausted",
        6 => "Critical write failures",
        7 => "Data integrity scan required",
        _ => null,
    };
}
