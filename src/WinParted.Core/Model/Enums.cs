namespace WinParted.Core.Model;

public enum BusType
{
    Unknown,
    Ata,
    Sata,
    Sas,
    Scsi,
    Nvme,
    Usb,
    Sd,
    Mmc,
    Virtual,
    Raid,
    StorageSpaces,
    Other,
}

public enum PartitionStyle
{
    Uninitialized,
    Mbr,
    Gpt,
}

public enum HealthStatus
{
    Healthy,
    Warning,
    Unhealthy,
    Unknown,
}

public enum FileSystemKind
{
    /// <summary>No file system, or one Windows reports as RAW.</summary>
    None,
    Ntfs,
    ReFS,
    Fat,
    Fat32,
    ExFat,
    Udf,
    Csvfs,

    /// <summary>A file system Windows reports by name but WinParted doesn't recognize.</summary>
    Other,
}

public enum BitLockerState
{
    NotEncrypted,
    Unlocked,
    Locked,

    /// <summary>The lock state could not be determined. Treated as protected (fail closed).</summary>
    Unknown,
}

/// <summary>What a partition contains, from the user's point of view.</summary>
public enum PartitionContent
{
    /// <summary>A volume with a file system Windows can read.</summary>
    Volume,

    /// <summary>A Windows data partition without a file system.</summary>
    Unformatted,

    /// <summary>A partition type reserved by Windows or firmware (EFI, MSR, Recovery, LDM).</summary>
    Reserved,

    /// <summary>Content Windows can't read, e.g. a Linux partition.</summary>
    Foreign,
}
