using System.Collections.Immutable;
using System.Globalization;
using WinParted.Core.Actions;
using WinParted.Core.Formatting;
using WinParted.Core.Model;
using WinParted.Core.Protection;
using WinParted.Core.Selections;
using WinParted.Core.Text;

namespace WinParted.Core.Presentation;

/// <summary>
/// Turns the snapshot into everything the main window shows (mvp-features.md sections 3-5): disk
/// list entries, layout segments, details and actions. Pure .NET, so it is unit-tested; the WinUI
/// layer only binds to the results.
/// </summary>
public static class Presenter
{
    private static readonly ImmutableArray<(ActionKind Kind, string Group)> PartitionGroups =
    [
        (ActionKind.FormatVolume, "Ui.Group.Contents"),
        (ActionKind.DeletePartition, "Ui.Group.Contents"),
        (ActionKind.ShrinkVolume, "Ui.Group.Size"),
        (ActionKind.ExtendVolume, "Ui.Group.Size"),
        (ActionKind.ChangeDriveLetter, "Ui.Group.NameAndLetter"),
        (ActionKind.ChangeLabel, "Ui.Group.NameAndLetter"),
    ];

    // ----- Disks ---------------------------------------------------------------------------------

    public static DiskView Disk(Disk disk, bool isBusy = false)
    {
        var protection = ProtectionClassifier.Classify(disk);
        var chips = ImmutableArray.CreateBuilder<Chip>();
        if (protection.IsSystemDisk)
        {
            chips.Add(new Chip(T("Ui.Chip.SystemDisk"), ChipTone.Neutral, ChipIcon.Lock));
        }

        if (disk.IsOffline)
        {
            chips.Add(new Chip(T("Ui.Chip.Offline"), ChipTone.Warning, ChipIcon.Power));
        }

        if (disk.Style == PartitionStyle.Uninitialized && !disk.IsOffline)
        {
            chips.Add(new Chip(T("Ui.Chip.NotInitialized"), ChipTone.Neutral, ChipIcon.NotInitialized));
        }

        if (protection.Flags.HasFlag(DiskProtectionFlags.ReadOnly))
        {
            chips.Add(new Chip(T("Ui.Chip.ReadOnly"), ChipTone.Neutral, ChipIcon.Lock));
        }

        if (protection.IsWholeDiskLocked && !protection.Flags.HasFlag(DiskProtectionFlags.ReadOnly))
        {
            chips.Add(new Chip(Texts.Role(protection.Role!.Value), ChipTone.Neutral, ChipIcon.Lock));
        }

        if (disk.Health is HealthStatus.Warning or HealthStatus.Unhealthy)
        {
            chips.Add(HealthChip(disk.Health));
        }

        if (isBusy)
        {
            chips.Add(new Chip(T("Ui.Chip.Busy"), ChipTone.Accent, ChipIcon.Busy));
        }

        var header = ImmutableArray.CreateBuilder<Chip>();
        header.AddRange(chips.Where(c => c.Icon is ChipIcon.Lock && c.Text == T("Ui.Chip.SystemDisk")));
        header.Add(new Chip(SizeFormatter.Format(disk.Size), ChipTone.Neutral, ChipIcon.Drive));
        header.Add(new Chip(Bus(disk.Bus), ChipTone.Neutral, BusIcon(disk.Bus)));
        header.Add(new Chip(Style(disk.Style), ChipTone.Neutral, ChipIcon.PartitionStyle));
        header.Add(disk.IsOffline
            ? new Chip(T("Ui.Chip.Offline"), ChipTone.Warning, ChipIcon.Power)
            : new Chip(T("Ui.Chip.Online"), ChipTone.Success, ChipIcon.Dot));
        header.Add(HealthChip(disk.Health));
        header.AddRange(chips.Where(c => c.Icon is ChipIcon.Lock && c.Text != T("Ui.Chip.SystemDisk")));
        if (isBusy)
        {
            header.Add(new Chip(T("Ui.Chip.Busy"), ChipTone.Accent, ChipIcon.Busy));
        }

        var title = F("Ui.DiskTitle", disk.Number);
        var meta = $"{Bus(disk.Bus)} · {Style(disk.Style)}";
        var state = disk.IsOffline ? DiskState.Offline : disk.Style == PartitionStyle.Uninitialized ? DiskState.NotInitialized : DiskState.Normal;
        var automation = string.Join(", ", new[] { title, disk.FriendlyName, SizeFormatter.Format(disk.Size), Bus(disk.Bus), Style(disk.Style) }
            .Concat(chips.Select(c => c.Text)));

        return new DiskView(
            disk.Id,
            title,
            disk.FriendlyName,
            SizeFormatter.Format(disk.Size),
            Bus(disk.Bus),
            Style(disk.Style),
            meta,
            BusIcon(disk.Bus),
            chips.ToImmutable(),
            header.ToImmutable(),
            Segments(disk),
            state,
            disk.OfflineReason,
            automation);
    }

    // ----- Segments ------------------------------------------------------------------------------

    public static ImmutableArray<SegmentView> Segments(Disk disk) => [.. disk.Layout.Select(Segment)];

    public static SegmentView Segment(LayoutSegment segment) => segment switch
    {
        PartitionSegment p => PartitionSegment(p.Partition),
        UnallocatedSegment u => new SegmentView(
            new UnallocatedRef(u.Disk, u.Offset),
            SegmentKind.Unallocated,
            u.Size,
            T("Ui.Segment.Unallocated"),
            T("Ui.Segment.Unallocated"),
            SizeFormatter.Format(u.Size),
            T("Ui.Segment.NotPartitioned"),
            0,
            IsLocked: false,
            IsEncrypted: false,
            Role: null,
            AutomationName: $"{T("Ui.Segment.Unallocated")}, {SizeFormatter.Format(u.Size)}"),
        _ => throw new ArgumentOutOfRangeException(nameof(segment)),
    };

    private static SegmentView PartitionSegment(Partition p)
    {
        var protection = ProtectionClassifier.Classify(p);
        var content = p.Content;
        var volume = p.Volume;
        var size = SizeFormatter.Format(p.Size);
        var title = DisplayNames.ForPartition(p);
        var encrypted = volume?.BitLocker == BitLockerState.Unlocked;

        var kind = protection.Flags.HasFlag(PartitionProtectionFlags.BitLockerLocked) ? SegmentKind.BitLockerLocked
            : content == PartitionContent.Reserved || protection.IsFullyLocked ? SegmentKind.Protected
            : content == PartitionContent.Foreign ? SegmentKind.Unknown
            : content == PartitionContent.Unformatted ? SegmentKind.Unformatted
            : SegmentKind.Volume;

        var fs = kind switch
        {
            SegmentKind.Unknown => T("Ui.Segment.Unknown"),
            SegmentKind.BitLockerLocked => "BitLocker",
            SegmentKind.Unformatted => "RAW",
            _ => volume is { FileSystem: not FileSystemKind.None } v ? Texts.FileSystem(v.FileSystem, v.FileSystemName) : T("Ui.Value.None"),
        };
        var line2 = encrypted ? $"{T("Ui.Chip.Encrypted")} · {fs} · {size}" : $"{fs} · {size}";
        var showsUsage = kind == SegmentKind.Volume && volume is { Size: > 0 };
        var fraction = showsUsage ? (double)volume!.UsedSize / volume.Size : 0;
        var percent = (int)Math.Round(fraction * 100);

        var line3 = kind switch
        {
            SegmentKind.Volume when showsUsage => F("Ui.Segment.Used", SizeFormatter.Format(volume!.UsedSize), percent),
            SegmentKind.Unknown => T("Ui.Segment.Unreadable"),
            SegmentKind.Unformatted => T("Ui.Segment.NoFileSystem"),
            SegmentKind.BitLockerLocked => T("Ui.Segment.LockedBitLocker"),
            _ => T("Ui.Segment.Protected"),
        };

        var role = protection.Role is { } r ? Texts.Role(r) : null;
        var automation = string.Join(", ", new[]
        {
            title,
            $"{fs}, {size}",
            showsUsage ? $"{SizeFormatter.Format(volume!.UsedSize)} used" : null,
            protection.IsProtected ? T("Ui.Segment.Protected.Suffix") : null,
        }.OfType<string>()) + (role is null ? "" : $". {role}");

        return new SegmentView(
            new PartitionRef(p.Id),
            kind,
            p.Size,
            title,
            ShortTitle(p, title),
            line2,
            line3,
            fraction,
            protection.IsProtected,
            encrypted,
            role,
            automation);
    }

    private static string ShortTitle(Partition p, string title) =>
        KnownPartitionTypes.IsEfiSystem(p) ? "EFI"
        : KnownPartitionTypes.IsMicrosoftReserved(p) ? "MSR"
        : KnownPartitionTypes.IsRecovery(p) ? "Rec."
        : p.DriveLetter is char l ? $"{l}:"
        : title;

    // ----- Details -------------------------------------------------------------------------------

    public static DetailsView? Details(StorageSnapshot snapshot, Selection selection)
    {
        if (selection.Disk is not DiskId diskId || snapshot.FindDisk(diskId) is not { } disk)
        {
            return null;
        }

        return selection.Segment switch
        {
            PartitionRef pr when snapshot.FindPartition(pr.Id) is { } p => PartitionDetails(p),
            UnallocatedRef ur when disk.UnallocatedSegments.FirstOrDefault(s => s.Offset == ur.Offset) is { } u => UnallocatedDetails(u),
            _ => DiskDetails(disk),
        };
    }

    private static DetailsView PartitionDetails(Partition p)
    {
        var segment = PartitionSegment(p);
        var protection = ProtectionClassifier.Classify(p);
        var v = p.Volume;
        var rows = ImmutableArray.CreateBuilder<DetailRow>();
        rows.Add(new(T("Ui.Detail.Partition"), p.Number.ToString(CultureInfo.CurrentCulture)));
        rows.Add(new(T("Ui.Detail.Type"), TypeDescription(p)));
        rows.Add(new(T("Ui.Detail.StartsAt"), SizeFormatter.Format(p.Offset)));
        rows.Add(new(T("Ui.Detail.Size"), SizeFormatter.Format(p.Size)));
        rows.Add(new(T("Ui.Detail.FileSystem"), segment.Kind == SegmentKind.Volume || segment.Kind == SegmentKind.Protected
            ? v is { FileSystem: not FileSystemKind.None } ? Texts.FileSystem(v.FileSystem, v.FileSystemName) : T("Ui.Value.None")
            : segment.Line2.Split(" · ")[0]));
        if (v?.Label is { } label)
        {
            rows.Add(new(T("Ui.Detail.Label"), label));
        }

        rows.Add(new(T("Ui.Detail.DriveLetter"), p.DriveLetter is char l ? $"{l}:" : T("Ui.Value.NoDriveLetter")));
        var mounts = p.AccessPaths.Where(a => !a.StartsWith(@"\\?\Volume", StringComparison.OrdinalIgnoreCase)).ToList();
        if (mounts.Count > 0)
        {
            rows.Add(new(T("Ui.Detail.MountPaths"), string.Join(", ", mounts)));
        }

        var flags = ImmutableArray.CreateBuilder<string>();
        if (p.IsSystem)
        {
            flags.Add(T("Ui.Flag.System"));
        }

        if (p.IsBoot)
        {
            flags.Add(T("Ui.Flag.Boot"));
        }

        if (p.IsHidden)
        {
            flags.Add(T("Ui.Flag.Hidden"));
        }

        if (p.IsActive)
        {
            flags.Add(T("Ui.Flag.Active"));
        }

        if (p.IsReadOnly)
        {
            flags.Add(T("Ui.Flag.ReadOnly"));
        }

        if (v?.BitLocker == BitLockerState.Unlocked)
        {
            flags.Add(T("Ui.Flag.Encrypted"));
        }

        if (v?.BitLocker == BitLockerState.Locked)
        {
            flags.Add(T("Ui.Flag.Locked"));
        }

        var hasUsage = segment.Kind == SegmentKind.Volume && v is { Size: > 0 };
        var role = segment.Role ?? segment.Kind switch
        {
            SegmentKind.Unformatted => T("Ui.Role.Unformatted"),
            SegmentKind.Unknown => T("Ui.Role.Foreign"),
            _ => T("Ui.Role.Volume"),
        };

        return new DetailsView(
            segment.Title,
            role,
            protection.IsProtected,
            IsDisk: false,
            hasUsage,
            segment.UsedFraction,
            hasUsage ? $"{SizeFormatter.Format(v!.UsedSize)} {T("Ui.Detail.Used").ToLowerInvariant()} · {SizeFormatter.Format(v.SizeRemaining)} {T("Ui.Detail.Free").ToLowerInvariant()}" : "",
            hasUsage ? $"{(int)Math.Round(segment.UsedFraction * 100)}%" : "",
            Hint: null,
            flags.ToImmutable(),
            rows.ToImmutable());
    }

    private static DetailsView UnallocatedDetails(UnallocatedSegment u) => new(
        T("Ui.Segment.Unallocated"),
        T("Ui.Role.Unallocated"),
        IsLocked: false,
        IsDisk: false,
        HasUsage: false,
        0,
        "",
        "",
        T("Ui.Hint.Unallocated"),
        [],
        [
            new(T("Ui.Detail.Size"), SizeFormatter.Format(u.Size)),
            new(T("Ui.Detail.StartsAt"), SizeFormatter.Format(u.Offset)),
        ]);

    private static DetailsView DiskDetails(Disk disk)
    {
        var protection = ProtectionClassifier.Classify(disk);
        var rows = ImmutableArray.CreateBuilder<DetailRow>();
        rows.Add(new(T("Ui.Detail.Size"), SizeFormatter.Format(disk.Size)));
        rows.Add(new(T("Ui.Detail.BusType"), Bus(disk.Bus)));
        rows.Add(new(T("Ui.Detail.PartitionStyle"), Style(disk.Style)));
        rows.Add(new(T("Ui.Detail.Status"), disk.IsOffline ? T("Ui.Value.Offline") : T("Ui.Value.Online")));
        if (disk.IsOffline && disk.OfflineReason is { } reason)
        {
            rows.Add(new(T("Ui.Detail.OfflineReason"), reason));
        }

        rows.Add(new(T("Ui.Detail.Health"), Health(disk.Health)));
        if (!string.IsNullOrWhiteSpace(disk.SerialNumber))
        {
            rows.Add(new(T("Ui.Detail.SerialNumber"), disk.SerialNumber!, Copyable: true));
        }

        rows.Add(new(T("Ui.Detail.SectorSize"), F("Ui.Value.Bytes", disk.LogicalSectorSize)));
        if (!string.IsNullOrWhiteSpace(disk.Location))
        {
            rows.Add(new(T("Ui.Detail.Location"), disk.Location!));
        }

        rows.Add(new(T("Ui.Detail.Partitions"), disk.Partitions.Length == 0 ? T("Ui.Value.None") : disk.Partitions.Length.ToString(CultureInfo.CurrentCulture)));
        rows.Add(new(T("Ui.Detail.Unallocated"), disk.UnallocatedSize == 0 ? T("Ui.Value.None") : SizeFormatter.Format(disk.UnallocatedSize)));

        return new DetailsView(
            F("Ui.DiskTitle", disk.Number),
            protection.Role is { } r ? Texts.Role(r) : T("Ui.Role.Disk"),
            protection.IsProtected,
            IsDisk: true,
            HasUsage: false,
            0,
            "",
            "",
            Hint: null,
            [],
            rows.ToImmutable());
    }

    private static string TypeDescription(Partition p)
    {
        if (p.GptType is Guid gpt)
        {
            var name = KnownPartitionTypes.IsReserved(p) ? DisplayNames.TypeName(p)
                : gpt == KnownPartitionTypes.GptBasicData ? T("Ui.Type.BasicData")
                : p.Content == PartitionContent.Foreign ? DisplayNames.TypeName(p)
                : T("Ui.Type.Other");
            return F("Ui.Type.Gpt", name);
        }

        if (p.MbrType is byte mbr)
        {
            var name = KnownPartitionTypes.IsReserved(p) ? DisplayNames.TypeName(p)
                : KnownPartitionTypes.IsBasicData(p) ? T("Ui.Type.Primary")
                : T("Ui.Type.Other");
            return F("Ui.Type.Mbr", name, mbr);
        }

        return T("Ui.Type.Other");
    }

    // ----- Actions -------------------------------------------------------------------------------

    /// <summary>
    /// Actions for the selection, grouped as in the design: disk actions ("Disk actions") when only a
    /// disk is selected, otherwise the partition or unallocated-space actions ("Actions").
    /// </summary>
    public static ActionsView? Actions(StorageSnapshot snapshot, Selection selection, bool isBusy)
    {
        var states = ActionAvailability.ForSelection(snapshot, selection, isBusy);
        if (states.IsEmpty)
        {
            return null;
        }

        if (selection.Segment is null)
        {
            return new ActionsView(T("Ui.Action.DiskActions"), [new ActionGroup(T("Ui.Group.Disk"), [.. states.Select(Item)])]);
        }

        var groups = states
            .Select(s => (State: s, Group: GroupKey(s.Kind)))
            .GroupBy(x => x.Group)
            .Select(g => new ActionGroup(T(g.Key), [.. g.Select(x => Item(x.State))]))
            .ToImmutableArray();
        return new ActionsView(T("Ui.Action.Actions"), groups);
    }

    /// <summary>Disk-level buttons for the header, with disabled reasons merged into summary lines.</summary>
    public static DiskActionsView DiskActions(StorageSnapshot snapshot, DiskId disk, bool isBusy)
    {
        var states = ActionAvailability.ForSelection(snapshot, Selection.OfDisk(disk), isBusy);
        var items = states.Select(Item).ToImmutableArray();
        var reasons = items
            .Where(i => !i.IsEnabled)
            .GroupBy(i => i.Reason!)
            .Select(g => new ReasonSummary(JoinAnd([.. g.Select(i => i.Label)]), g.Key))
            .ToImmutableArray();
        return new DiskActionsView(items, reasons);
    }

    public static string ReasonSummaryText(ReasonSummary summary) => F("Ui.ReasonSummary", summary.Labels, summary.Reason);

    private static ActionItem Item(ActionState s) =>
        new(s.Kind, T($"Ui.Action.{s.Kind}"), s.IsEnabled, s.DisabledReason?.Text);

    private static string GroupKey(ActionKind kind) =>
        kind == ActionKind.CreatePartition ? "Ui.Group.NewPartition"
        : PartitionGroups.FirstOrDefault(g => g.Kind == kind).Group ?? "Ui.Group.Disk";

    public static string JoinAnd(IReadOnlyList<string> items) => items.Count switch
    {
        0 => "",
        1 => items[0],
        _ => F("Ui.ListAnd", string.Join(", ", items.Take(items.Count - 1)), items[^1]),
    };

    // ----- Helpers -------------------------------------------------------------------------------

    public static string Bus(BusType bus) => T($"Ui.Bus.{bus}");

    public static string Style(PartitionStyle style) => T($"Ui.Style.{style}");

    public static string Health(HealthStatus health) => T($"Ui.Health.{health}");

    private static Chip HealthChip(HealthStatus health) => health switch
    {
        HealthStatus.Healthy => new Chip(Health(health), ChipTone.Success, ChipIcon.Check),
        HealthStatus.Warning => new Chip(Health(health), ChipTone.Warning, ChipIcon.Warning),
        HealthStatus.Unhealthy => new Chip(Health(health), ChipTone.Critical, ChipIcon.Error),
        _ => new Chip(Health(health), ChipTone.Neutral, ChipIcon.Warning),
    };

    private static ChipIcon BusIcon(BusType bus) => bus switch
    {
        BusType.Usb => ChipIcon.Usb,
        BusType.Sd or BusType.Mmc => ChipIcon.Sd,
        BusType.Virtual => ChipIcon.Virtual,
        _ => ChipIcon.Drive,
    };

    private static string T(string key) => Texts.Get(key);

    private static string F(string key, params object[] args) => string.Format(CultureInfo.CurrentCulture, Texts.Get(key), args);
}
