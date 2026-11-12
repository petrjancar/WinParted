namespace WinParted.Core.Model;

public sealed record Volume
{
    public required VolumeId Id { get; init; }

    public string? Label { get; init; }

    public FileSystemKind FileSystem { get; init; }

    /// <summary>File system name as reported by Windows, for display when <see cref="FileSystem"/> is Other.</summary>
    public string? FileSystemName { get; init; }

    public ulong Size { get; init; }

    public ulong SizeRemaining { get; init; }

    public BitLockerState BitLocker { get; init; } = BitLockerState.NotEncrypted;

    /// <summary>True when the volume holds the running Windows installation.</summary>
    public bool IsWindowsVolume { get; init; }

    /// <summary>
    /// True when Windows keeps a pagefile or crash dump file here. Null means it could not be
    /// determined, which is treated as protected (fail closed).
    /// </summary>
    public bool? HasPagefileOrDump { get; init; } = false;

    public ulong UsedSize => SizeRemaining >= Size ? 0 : Size - SizeRemaining;

    public bool IsReadable => FileSystem is not FileSystemKind.None and not FileSystemKind.Other;

    public bool IsResizable => FileSystem is FileSystemKind.Ntfs or FileSystemKind.ReFS;
}
