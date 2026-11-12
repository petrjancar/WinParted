using System.Collections.Immutable;
using System.Globalization;
using WinParted.Core.Actions;
using WinParted.Core.Formatting;
using WinParted.Core.Layout;
using WinParted.Core.Model;
using WinParted.Core.Operations;
using WinParted.Core.Text;

namespace WinParted.Core.Presentation;

public enum SizeUnit
{
    MB,
    GB,
}

/// <summary>Size inputs in dialogs: a number plus MB/GB, aligned down to whole megabytes.</summary>
public static class SizeInput
{
    public const ulong MiB = 1024UL * 1024;
    public const ulong GiB = 1024 * MiB;

    public static SizeUnit BestUnit(ulong bytes) => bytes >= GiB ? SizeUnit.GB : SizeUnit.MB;

    /// <summary>
    /// A size limit with the same precision as the input field ("14.43 GB"), so a limit shown to the
    /// user is exactly what can be typed, unlike the three-digit display format ("14.4 GB").
    /// </summary>
    public static string Precise(ulong bytes)
    {
        var unit = BestUnit(bytes);
        return $"{FromBytes(bytes, unit).ToString("0.##", CultureInfo.CurrentCulture)} {unit}";
    }

    public static double FromBytes(ulong bytes, SizeUnit unit) =>
        Math.Floor(bytes / (double)(unit == SizeUnit.GB ? GiB : MiB) * 100) / 100;

    /// <summary>Converts the entered value to bytes, rounded down to whole megabytes.</summary>
    public static ulong ToBytes(double value, SizeUnit unit)
    {
        if (double.IsNaN(value) || value <= 0)
        {
            return 0;
        }

        var bytes = value * (unit == SizeUnit.GB ? GiB : MiB);
        return bytes >= ulong.MaxValue ? ulong.MaxValue : (ulong)bytes / MiB * MiB;
    }

    /// <summary>Returns why the size is invalid, or null.</summary>
    public static string? Validate(double value, SizeUnit unit, ulong min, ulong max, string? tooLargeText = null) =>
        Validate(double.IsNaN(value) ? null : ToBytes(value, unit), min, max, tooLargeText);

    /// <summary>Returns why the size is invalid, or null. A null size means the text isn't a number.</summary>
    public static string? Validate(ulong? size, ulong min, ulong max, string? tooLargeText = null)
    {
        if (size is not ulong bytes)
        {
            return Dialogs.T("Dlg.SizeInvalid");
        }

        if (bytes < min)
        {
            return min <= MiB ? Dialogs.T("Dlg.SizeTooSmall") : Dialogs.F("Dlg.SizeOutOfRange", Precise(min), Precise(max));
        }

        return bytes > max ? tooLargeText ?? Dialogs.F("Dlg.SizeOutOfRange", Precise(min), Precise(max)) : null;
    }
}

/// <summary>A choice in a drop-down: text and the value it stands for.</summary>
public sealed record Choice<T>(string Text, T Value, string? DisabledReason = null)
{
    public bool IsEnabled => DisabledReason is null;

    public override string ToString() => Text;
}

/// <summary>Shared helpers and the target captured when a dialog opens (design D4).</summary>
public abstract class DialogModel
{
    protected DialogModel(StorageSnapshot snapshot, ActionTarget target)
    {
        Target = target;
        Fingerprint = TargetFingerprint.Of(snapshot, target) ?? throw new ArgumentException("Target not in snapshot.", nameof(target));
    }

    /// <summary>What the dialog acts on; it closes itself if this changes (spec device-change-detection).</summary>
    public ActionTarget Target { get; }

    /// <summary>The target as the user saw it when the dialog opened.</summary>
    public TargetFingerprint Fingerprint { get; }

    public abstract string Title { get; }

    public abstract string PrimaryText { get; }

    /// <summary>Destructive dialogs use the red primary button (mvp-features.md 6).</summary>
    public virtual bool IsDestructive => false;

    /// <summary>
    /// Whether Enter confirms (the primary button is the highlighted default once the input is valid). False
    /// only for a destructive dialog with nothing to type, where Cancel is the default (mvp-features.md 6).
    /// </summary>
    public virtual bool EnterConfirms => true;

    /// <summary>True when the target no longer matches what the dialog was opened for.</summary>
    public bool IsStale(StorageSnapshot snapshot) => TargetFingerprint.Of(snapshot, Target) != Fingerprint;

    protected static string Identity(Disk disk) =>
        Dialogs.F("Dlg.Identity", disk.FriendlyName, SizeFormatter.Format(disk.Size), Presenter.Bus(disk.Bus));
}

internal static class Dialogs
{
    public static string T(string key) => Texts.Get(key);

    public static string F(string key, params object[] args) => string.Format(CultureInfo.CurrentCulture, Texts.Get(key), args);

    public static string Name(Partition p) => DisplayNames.ForPartition(p);

    public static string UsedSuffix(Partition p) =>
        p.Volume is { Size: > 0 } v && p.Content == PartitionContent.Volume ? F("Dlg.UsedSuffix", SizeFormatter.Format(v.UsedSize)) : "";
}

// ----- Disk dialogs ------------------------------------------------------------------------------

public sealed class InitializeDialogModel(StorageSnapshot snapshot, Disk disk) : DialogModel(snapshot, new DiskTarget(disk.Id))
{
    private const ulong TwoTerabytes = 2UL * 1024 * SizeInput.GiB;

    public Disk Disk { get; } = disk;

    public override string Title => Dialogs.F("Dlg.Initialize.Title", Disk.Number);

    public override string PrimaryText => Dialogs.T("Dlg.Initialize.Primary");

    public string IdentityText => Identity(Disk);

    /// <summary>Warning shown when MBR is chosen for a disk larger than 2 TB.</summary>
    public string? MbrWarning => Disk.Size > TwoTerabytes ? Dialogs.F("Dlg.Initialize.MbrWarning", SizeFormatter.Format(Disk.Size)) : null;

    public static string ResultHint(PartitionStyle style) =>
        Dialogs.T(style == PartitionStyle.Gpt ? "Dlg.Initialize.GptResult" : "Dlg.Initialize.MbrResult");

    public OperationRequest Request(PartitionStyle style) => new InitializeDiskRequest((DiskTarget)Target, Fingerprint, style);
}

public sealed class OfflineDialogModel(StorageSnapshot snapshot, Disk disk) : DialogModel(snapshot, new DiskTarget(disk.Id))
{
    public override string Title => Dialogs.F("Dlg.Offline.Title", disk.Number);

    public override string PrimaryText => Dialogs.T("Dlg.Offline.Primary");

    public string IdentityText => Identity(disk);

    public string Body
    {
        get
        {
            var letters = disk.Partitions.Select(p => p.DriveLetter).OfType<char>().Select(l => $"{l}:").ToList();
            return letters.Count == 0 ? Dialogs.T("Dlg.Offline.BodyNoVolumes") : Dialogs.F("Dlg.Offline.Body", string.Join(", ", letters));
        }
    }

    public OperationRequest Request() => new SetDiskOnlineRequest((DiskTarget)Target, Fingerprint, Online: false);
}

public sealed class ClearDialogModel(StorageSnapshot snapshot, Disk disk) : DialogModel(snapshot, new DiskTarget(disk.Id))
{
    public Disk Disk { get; } = disk;

    public override string Title => Dialogs.F("Dlg.Clear.Title", Disk.Number);

    public override string PrimaryText => Dialogs.T("Dlg.Clear.Primary");

    public override bool IsDestructive => true;

    public string IdentityText => Identity(Disk);

    public string Warning => Dialogs.T("Dlg.Clear.Warning");

    public string ConfirmLabel => Dialogs.F("Dlg.Clear.Confirm", Disk.Number);

    /// <summary>The labelled partitions with used space, so the user recognises what will be lost.</summary>
    public ImmutableArray<SegmentView> Segments => Presenter.Segments(Disk);

    /// <summary>The primary button is enabled only when exactly the disk number is typed (spec disk-operations).</summary>
    public bool IsConfirmed(string? typed) => typed?.Trim() == Disk.Number.ToString(CultureInfo.InvariantCulture);

    public OperationRequest Request() => new ClearDiskRequest((DiskTarget)Target, Fingerprint);
}

// ----- Partition dialogs --------------------------------------------------------------------------

/// <summary>File system, label, letter and quick/full choices shared by Create and Format.</summary>
public static class FormatChoices
{
    public static ImmutableArray<Choice<FileSystemKind>> FileSystems(ulong size) =>
        [.. FileSystemRules.FormatOptions(size).Select(o => new Choice<FileSystemKind>(Texts.FileSystem(o.FileSystem), o.FileSystem, o.DisabledReason?.Text))];

    /// <summary>Free letters plus "No drive letter"; the current letter (if any) comes first.</summary>
    public static ImmutableArray<Choice<char?>> Letters(SystemFacts facts, char? current = null)
    {
        var list = new List<Choice<char?>>();
        if (current is char c)
        {
            list.Add(new(Dialogs.F("Dlg.Letter.Current", $"{c}:"), c));
        }

        list.AddRange(facts.FreeDriveLetters.Where(l => l != current).Select(l => new Choice<char?>($"{l}:", l)));
        list.Add(new(Dialogs.T("Dlg.NoDriveLetter"), null));
        return [.. list];
    }

    /// <summary>Why a full format isn't possible here, or null (spec partition-operations).</summary>
    public static string? FullFormatReason(FileSystemKind fs, BusType bus) => FileSystemRules.FullFormatReason(fs, bus)?.Text;

    public static string? LabelError(FileSystemKind fs, string? label) =>
        string.IsNullOrEmpty(label) ? null : FileSystemRules.ValidateLabel(fs, label)?.Text;
}

public sealed class CreateDialogModel(StorageSnapshot snapshot, Disk disk, UnallocatedSegment region)
    : DialogModel(snapshot, new UnallocatedTarget(disk.Id, region.Offset))
{
    public Disk Disk { get; } = disk;

    public UnallocatedSegment Region { get; } = region;

    public override string Title => Dialogs.F("Dlg.Create.Title", Disk.Number);

    public override string PrimaryText => Dialogs.T("Dlg.Create.Primary");

    public string Subtitle => Dialogs.F("Dlg.Create.Subtitle", Disk.FriendlyName, SizeFormatter.Format(Region.Size));

    public ulong MaxSize => Region.Size / SizeInput.MiB * SizeInput.MiB;

    public SizeUnit DefaultUnit => SizeInput.BestUnit(MaxSize);

    public string RangeText => Dialogs.F("Dlg.Range", "1 MB", SizeInput.Precise(MaxSize));

    public string? SizeError(double value, SizeUnit unit) =>
        SizeInput.Validate(value, unit, SizeInput.MiB, MaxSize, Dialogs.F("Dlg.SizeTooLarge", SizeInput.Precise(MaxSize)));

    public string? SizeError(ulong? bytes) =>
        SizeInput.Validate(bytes, SizeInput.MiB, MaxSize, Dialogs.F("Dlg.SizeTooLarge", SizeInput.Precise(MaxSize)));

    public ImmutableArray<Choice<FileSystemKind>> FileSystems(ulong size) => FormatChoices.FileSystems(size);

    public ImmutableArray<Choice<char?>> Letters(SystemFacts facts) => FormatChoices.Letters(facts);

    public BusType Bus => Disk.Bus;

    /// <param name="format">Null for "Don't format".</param>
    public OperationRequest Request(ulong size, FormatOptions? format, char? letter) =>
        new CreatePartitionRequest((UnallocatedTarget)Target, Fingerprint, Math.Min(size, MaxSize), format, format is null ? null : letter);
}

public sealed class DeleteDialogModel(StorageSnapshot snapshot, Partition partition, Disk disk)
    : DialogModel(snapshot, new PartitionTarget(partition.Id))
{
    public Partition Partition { get; } = partition;

    public override string Title => Partition.Content == PartitionContent.Foreign
        ? Dialogs.F("Dlg.Delete.TitleForeign", DisplayNames.TypeName(Partition), disk.Number)
        : Dialogs.F("Dlg.Delete.Title", Dialogs.Name(Partition));

    public override string PrimaryText => Dialogs.T("Dlg.Delete.Primary");

    public override bool IsDestructive => true;

    public override bool EnterConfirms => false;

    public SegmentView Segment => Presenter.Segment(new PartitionSegment(Partition));

    public string Body => Partition.Content switch
    {
        PartitionContent.Foreign => Dialogs.T("Dlg.Delete.Foreign"),
        PartitionContent.Unformatted => Dialogs.F("Dlg.Delete.BodyUnformatted", Dialogs.Name(Partition), disk.Number),
        _ => Dialogs.F("Dlg.Delete.Body", Dialogs.Name(Partition), disk.Number, Dialogs.UsedSuffix(Partition)),
    };

    /// <summary>Second line: the programs warning for volumes, the dual-boot note for foreign partitions.</summary>
    public string? Note => Partition.Content switch
    {
        PartitionContent.Foreign => Dialogs.T("Dlg.Delete.ForeignNote"),
        PartitionContent.Volume => Dialogs.F("Dlg.ProgramsWarning", Dialogs.Name(Partition)),
        _ => null,
    };

    public OperationRequest Request() => new DeletePartitionRequest((PartitionTarget)Target, Fingerprint);
}

public sealed class FormatDialogModel(StorageSnapshot snapshot, Partition partition, Disk disk)
    : DialogModel(snapshot, new PartitionTarget(partition.Id))
{
    public Partition Partition { get; } = partition;

    public override string Title => Dialogs.F("Dlg.Format.Title", Dialogs.Name(Partition));

    public override string PrimaryText => Dialogs.T("Dlg.Format.Primary");

    public override bool IsDestructive => true;

    public string Body => Dialogs.F("Dlg.Format.Body", Dialogs.Name(Partition), Dialogs.UsedSuffix(Partition));

    public string ProgramsWarning => Dialogs.F("Dlg.ProgramsWarning", Dialogs.Name(Partition));

    public string IdentityText => $"{Dialogs.F("Ui.DiskTitle", disk.Number)} · {disk.FriendlyName}";

    public string? KeepsLetterText => Partition.DriveLetter is char l ? Dialogs.F("Dlg.Format.KeepsLetter", $"{l}:") : null;

    public BusType Bus => disk.Bus;

    public ImmutableArray<Choice<FileSystemKind>> FileSystems => FormatChoices.FileSystems(Partition.Size);

    /// <summary>The current file system if it can be chosen, otherwise NTFS.</summary>
    public FileSystemKind DefaultFileSystem =>
        FileSystems.FirstOrDefault(c => c.Value == Partition.Volume?.FileSystem && c.IsEnabled)?.Value ?? FileSystemKind.Ntfs;

    public string DefaultLabel => Partition.Volume?.Label ?? "";

    public OperationRequest Request(FormatOptions options) => new FormatVolumeRequest((PartitionTarget)Target, Fingerprint, options);
}

public sealed class ShrinkDialogModel(StorageSnapshot snapshot, Partition partition)
    : DialogModel(snapshot, new PartitionTarget(partition.Id))
{
    public Partition Partition { get; } = partition;

    public override string Title => Dialogs.F("Dlg.Shrink.Title", Dialogs.Name(Partition));

    public override string PrimaryText => Dialogs.T("Dlg.Shrink.Primary");

    public string CurrentText => SizeFormatter.Format(Partition.Size);

    public string UsedText => Partition.Volume is { } v ? SizeFormatter.Format(v.UsedSize) : "";

    /// <summary>Largest allowed new size: at least 1 MB smaller than now.</summary>
    public ulong MaxSize => (Partition.Size - SizeInput.MiB) / SizeInput.MiB * SizeInput.MiB;

    /// <summary>Smallest allowed size, rounded up to whole megabytes.</summary>
    public static ulong AlignUp(ulong bytes) => (bytes + SizeInput.MiB - 1) / SizeInput.MiB * SizeInput.MiB;

    /// <summary>The explanation shown when the minimum is noticeably above the used space.</summary>
    public string? MinimumExplanation(ulong minimum) =>
        Partition.Volume is { } v && minimum > v.UsedSize + (v.Size / 20)
            ? Dialogs.F("Dlg.Shrink.MinimumExplained", SizeFormatter.Format(minimum))
            : null;

    public string? SizeError(double value, SizeUnit unit, ulong minimum) =>
        SizeInput.Validate(value, unit, AlignUp(minimum), MaxSize);

    public string? SizeError(ulong? bytes, ulong minimum) => SizeInput.Validate(bytes, AlignUp(minimum), MaxSize);

    public string FreesText(ulong newSize) =>
        Dialogs.F("Dlg.Shrink.Frees", SizeFormatter.Format(Partition.Size > newSize ? Partition.Size - newSize : 0));

    public string ResultText => Dialogs.F("Dlg.Shrink.Result", Dialogs.Name(Partition));

    public OperationRequest Request(ulong newSize) =>
        new ResizePartitionRequest((PartitionTarget)Target, Fingerprint, newSize, IsShrink: true);
}

public sealed class ExtendDialogModel(StorageSnapshot snapshot, Partition partition, Disk disk)
    : DialogModel(snapshot, new PartitionTarget(partition.Id))
{
    public Partition Partition { get; } = partition;

    public override string Title => Dialogs.F("Dlg.Extend.Title", Dialogs.Name(Partition));

    public override string PrimaryText => Dialogs.T("Dlg.Extend.Primary");

    public string CurrentText => SizeFormatter.Format(Partition.Size);

    /// <summary>The unallocated region directly after the partition (Extend is only enabled when it exists).</summary>
    public ulong FreeAfter =>
        disk.Layout.SkipWhile(s => s is not PartitionSegment ps || ps.Partition.Id != Partition.Id).Skip(1).FirstOrDefault() is UnallocatedSegment u
            ? u.Size
            : 0;

    public string FreeAfterText => SizeFormatter.Format(FreeAfter);

    /// <summary>Largest new size by the layout; Windows' own maximum is used when available.</summary>
    public ulong LayoutMax => (Partition.Size + FreeAfter) / SizeInput.MiB * SizeInput.MiB;

    public ulong MinSize => (Partition.Size / SizeInput.MiB * SizeInput.MiB) + SizeInput.MiB;

    public string? SizeError(double value, SizeUnit unit, ulong max) => SizeInput.Validate(value, unit, MinSize, max);

    public string? SizeError(ulong? bytes, ulong max) => SizeInput.Validate(bytes, MinSize, max);

    public string ResultText(ulong newSize) =>
        Dialogs.F("Dlg.Extend.Result", Dialogs.Name(Partition), SizeFormatter.Format(Partition.Size), SizeFormatter.Format(newSize));

    public OperationRequest Request(ulong newSize) =>
        new ResizePartitionRequest((PartitionTarget)Target, Fingerprint, newSize, IsShrink: false);
}

public sealed class LetterDialogModel(StorageSnapshot snapshot, Partition partition)
    : DialogModel(snapshot, new PartitionTarget(partition.Id))
{
    public Partition Partition { get; } = partition;

    public override string Title => Dialogs.F("Dlg.Letter.Title", Dialogs.Name(Partition));

    public override string PrimaryText => Dialogs.T("Dlg.Letter.Primary");

    public ImmutableArray<Choice<char?>> Letters { get; } = FormatChoices.Letters(snapshot.Facts, partition.DriveLetter);

    /// <summary>Preselects the first other letter, as in the design's "picking a new one" state.</summary>
    public Choice<char?> DefaultChoice => Letters.FirstOrDefault(c => c.Value != Partition.DriveLetter && c.Value is not null) ?? Letters[0];

    public string PrimaryTextFor(char? letter) =>
        letter is null && Partition.DriveLetter is not null ? Dialogs.T("Dlg.Letter.Remove")
        : Partition.DriveLetter is null ? Dialogs.T("Dlg.Letter.Assign")
        : PrimaryText;

    /// <summary>Side effects in plain words (DlgAccess board).</summary>
    public string? Warning(char? letter) => (Partition.DriveLetter, letter) switch
    {
        (char current, null) => Dialogs.F("Dlg.Letter.RemoveWarning", $"{current}:"),
        (char current, char next) when next != current => Dialogs.F("Dlg.Letter.ChangeWarning", $"{current}:"),
        _ => null,
    };

    public bool CanApply(char? letter) => letter != Partition.DriveLetter;

    public OperationRequest Request(char? letter) => new SetDriveLetterRequest((PartitionTarget)Target, Fingerprint, letter);
}

public sealed class LabelDialogModel(StorageSnapshot snapshot, Partition partition)
    : DialogModel(snapshot, new PartitionTarget(partition.Id))
{
    public Partition Partition { get; } = partition;

    public override string Title => Dialogs.F("Dlg.LabelDlg.Title", Dialogs.Name(Partition));

    public override string PrimaryText => Dialogs.T("Dlg.LabelDlg.Primary");

    public FileSystemKind FileSystem => Partition.Volume?.FileSystem ?? FileSystemKind.Ntfs;

    public string DefaultLabel => Partition.Volume?.Label ?? "";

    public int MaxLength => FileSystemRules.MaxLabelLength(FileSystem);

    public string? Error(string label) => FileSystemRules.ValidateLabel(FileSystem, label)?.Text;

    public string CountText(string label) =>
        Dialogs.F("Dlg.LabelDlg.Count", Texts.FileSystem(FileSystem), label.Length, MaxLength);

    public bool CanApply(string label) => Error(label) is null && label != DefaultLabel;

    public OperationRequest Request(string label) => new SetLabelRequest((PartitionTarget)Target, Fingerprint, label);
}

// ----- Running and results ------------------------------------------------------------------------

/// <summary>Texts while an operation runs and after it ends (mvp-features.md section 8).</summary>
public static class OperationTexts
{
    public static string Busy(StorageSnapshot snapshot, OperationRequest request) =>
        Dialogs.F($"Busy.{request.Kind}", Subject(snapshot, request));

    public static string Done(StorageSnapshot before, OperationRequest request) => request switch
    {
        InitializeDiskRequest r => Dialogs.F("Done.Initialize", DiskNumber(before, r.Target), Presenter.Style(r.Style)),
        FormatVolumeRequest r => Dialogs.F("Done.FormatVolume", Subject(before, request), Texts.FileSystem(r.Options.FileSystem)),
        ResizePartitionRequest r => Dialogs.F(r.IsShrink ? "Done.ShrinkVolume" : "Done.ExtendVolume", Subject(before, request), SizeFormatter.Format(r.NewSize)),
        SetDriveLetterRequest { Letter: char l } => Dialogs.F("Done.ChangeDriveLetter", $"{l}:"),
        SetDriveLetterRequest => Dialogs.T("Done.RemoveDriveLetter"),
        SetLabelRequest r => Dialogs.F("Done.ChangeLabel", r.Label),
        _ => Dialogs.F($"Done.{request.Kind}", Subject(before, request)),
    };

    public static string FailedTitle(StorageSnapshot snapshot, OperationRequest request) =>
        Dialogs.F("Fail.Title", Busy(snapshot, request));

    /// <summary>"Disk 3" for disk-level requests and Create, the volume name for partition requests.</summary>
    public static string Subject(StorageSnapshot snapshot, OperationRequest request) => request.Target switch
    {
        PartitionTarget t when snapshot.FindPartition(t.Partition) is { } p => DisplayNames.ForPartition(p),
        _ => DiskNumber(snapshot, request.Target).ToString(CultureInfo.CurrentCulture),
    };

    private static int DiskNumber(StorageSnapshot snapshot, ActionTarget target) => target switch
    {
        DiskTarget t => snapshot.FindDisk(t.Disk)?.Number ?? 0,
        UnallocatedTarget t => snapshot.FindDisk(t.Disk)?.Number ?? 0,
        PartitionTarget t => snapshot.FindDisk(t.Partition.Disk)?.Number ?? 0,
        _ => 0,
    };
}
