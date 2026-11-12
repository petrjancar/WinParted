using System.Collections.Immutable;
using WinParted.Core.Model;
using WinParted.Core.Text;

namespace WinParted.Core.Actions;

public sealed record FileSystemOption(FileSystemKind FileSystem, Reason? DisabledReason)
{
    public bool IsEnabled => DisabledReason is null;
}

/// <summary>Format options and label rules (spec partition-operations).</summary>
public static class FileSystemRules
{
    /// <summary>Windows' own tools stop at 32 GB for FAT32; WinParted follows them (SMP itself would go further).</summary>
    public const ulong MaxFat32Size = 32UL * 1024 * 1024 * 1024;

    private const string Fat32InvalidLabelChars = "*?.,;:/\\|+=<>[]\"";

    /// <summary>
    /// File systems offered when formatting a partition of the given size, in display order. The list
    /// is fixed because SMP's GetSupportedFileSystems proved unreliable (docs/spikes/smp-behavior.md).
    /// ReFS is not offered in the MVP: Windows 11 Pro only creates it as a Dev Drive.
    /// FAT32 is limited to 32 GB like in Windows itself, although SMP would format larger volumes.
    /// </summary>
    public static ImmutableArray<FileSystemOption> FormatOptions(ulong partitionSize) =>
    [
        new(FileSystemKind.Ntfs, null),
        new(FileSystemKind.ExFat, null),
        new(FileSystemKind.Fat32, partitionSize > MaxFat32Size ? Reason.Of(ReasonCode.Fat32TooLarge) : null),
    ];

    /// <summary>
    /// Whether a full (non-quick) format is possible. Windows refuses a full NTFS format on virtual
    /// disks (VHD/VHDX); everything else works (docs/spikes/smp-progress.md).
    /// </summary>
    public static Reason? FullFormatReason(FileSystemKind fs, BusType bus) =>
        fs == FileSystemKind.Ntfs && bus == BusType.Virtual ? Reason.Of(ReasonCode.FullFormatNotOnVirtualDisk) : null;

    public static int MaxLabelLength(FileSystemKind fs) => fs switch
    {
        FileSystemKind.Fat or FileSystemKind.Fat32 => 11,
        FileSystemKind.ExFat => 15,
        _ => 32,
    };

    /// <summary>Returns why the label is invalid for the file system, or null if it's fine.</summary>
    public static Reason? ValidateLabel(FileSystemKind fs, string label)
    {
        var fsName = Texts.FileSystem(fs);
        var max = MaxLabelLength(fs);
        if (label.Length > max)
        {
            return Reason.Of(ReasonCode.LabelTooLong, fsName, max.ToString(System.Globalization.CultureInfo.CurrentCulture));
        }

        if (fs is FileSystemKind.Fat or FileSystemKind.Fat32 && label.Any(Fat32InvalidLabelChars.Contains))
        {
            // The message lists every character except the quote, which it names in words.
            var listed = Fat32InvalidLabelChars.Where(c => c != '"').Select(c => c.ToString());
            return Reason.Of(ReasonCode.LabelInvalidCharacters, fsName, string.Join(' ', listed));
        }

        return null;
    }
}
