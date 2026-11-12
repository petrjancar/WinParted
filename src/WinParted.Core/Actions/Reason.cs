using System.Collections.Immutable;
using WinParted.Core.Text;

namespace WinParted.Core.Actions;

/// <summary>
/// Why an action or option is unavailable. Every code has a plain-language text in
/// <c>Resources/Texts.resx</c> (key <c>Reason.{Code}</c>), formatted with <see cref="Reason.Args"/>.
/// </summary>
public enum ReasonCode
{
    // Global
    OperationInProgress,
    TargetChanged,
    DiskReadOnly,
    DynamicDisk,
    StorageSpaces,
    DiskOffline,
    ProtectionUnknown,

    // Protected items
    SystemPartition,
    MicrosoftReserved,
    RecoveryPartition,
    WindowsVolume,
    PagefileVolume,
    BitLockerLocked,
    SystemDisk,

    // Operation-specific
    ExtendBlockedByPartition,
    ExtendFreeSpaceOnlyBefore,
    ExtendNoFreeSpace,
    ResizeUnsupportedFileSystem,
    ShrinkAtMinimum,
    ResizeNeedsFormat,
    AlreadyInitialized,
    RemovableMediaOffline,
    DiskAlreadyEmpty,
    MbrPartitionLimit,
    UnreadablePartition,
    NeedsFormatFirst,
    AllDriveLettersInUse,

    // Dialog options and validation
    Fat32TooLarge,
    FullFormatNotOnVirtualDisk,
    LabelTooLong,
    LabelInvalidCharacters,
}

public sealed record Reason(ReasonCode Code, ImmutableArray<string> Args)
{
    public static Reason Of(ReasonCode code, params string[] args) => new(code, [.. args]);

    /// <summary>The user-facing text in the current UI culture.</summary>
    public string Text => Texts.Format(this);

    public bool Equals(Reason? other) =>
        other is not null && Code == other.Code && Args.SequenceEqual(other.Args);

    public override int GetHashCode() => HashCode.Combine(Code, Args.Length);
}
