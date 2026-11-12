using System.Collections.Immutable;
using WinParted.Core.Actions;
using WinParted.Core.Model;
using WinParted.Core.Selections;

namespace WinParted.Core.Presentation;

/// <summary>Visual category of a layout segment (mvp-features.md section 4.3).</summary>
public enum SegmentKind
{
    Volume,
    Protected,
    Unformatted,
    Unknown,
    BitLockerLocked,
    Unallocated,
}

public enum ChipTone
{
    Neutral,
    Success,
    Accent,
    Warning,
    Critical,
}

public enum ChipIcon
{
    None,
    Lock,
    Power,
    NotInitialized,
    Warning,
    Error,
    Check,
    Dot,
    Busy,
    Drive,
    Usb,
    Sd,
    Virtual,
    PartitionStyle,
}

public sealed record Chip(string Text, ChipTone Tone, ChipIcon Icon = ChipIcon.None);

/// <summary>One entry in the disk list and the header of the selected disk.</summary>
public sealed record DiskView(
    DiskId Id,
    string Title,
    string Name,
    string SizeText,
    string BusText,
    string StyleText,
    string Meta,
    ChipIcon BusIcon,
    ImmutableArray<Chip> Chips,
    ImmutableArray<Chip> HeaderChips,
    ImmutableArray<SegmentView> Segments,
    DiskState State,
    string? OfflineReason,
    string AutomationName)
{
    public bool HasChips => !Chips.IsDefaultOrEmpty;
}

public enum DiskState
{
    Normal,
    NotInitialized,
    Offline,
}

/// <summary>One segment of the layout bar (partition or unallocated region).</summary>
public sealed record SegmentView(
    SegmentRef Ref,
    SegmentKind Kind,
    ulong Size,
    string Title,
    string ShortTitle,
    string Line2,
    string Line3,
    double UsedFraction,
    bool IsLocked,
    bool IsEncrypted,
    string? Role,
    string AutomationName);

public sealed record DetailRow(string Label, string Value, bool Copyable = false);

public sealed record DetailsView(
    string Title,
    string Role,
    bool IsLocked,
    bool IsDisk,
    bool HasUsage,
    double UsedFraction,
    string UsageText,
    string PercentText,
    string? Hint,
    ImmutableArray<string> Flags,
    ImmutableArray<DetailRow> Rows);

public sealed record ActionItem(ActionKind Kind, string Label, bool IsEnabled, string? Reason);

public sealed record ActionGroup(string Name, ImmutableArray<ActionItem> Items);

/// <summary>Disabled disk actions sharing a reason, shown as one line ("Initialize and Clear disk: ...").</summary>
public sealed record ReasonSummary(string Labels, string Reason);

public sealed record ActionsView(string Title, ImmutableArray<ActionGroup> Groups);

public sealed record DiskActionsView(ImmutableArray<ActionItem> Items, ImmutableArray<ReasonSummary> Reasons);
