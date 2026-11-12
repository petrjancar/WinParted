using System.Globalization;
using WinParted.Core.Model;
using WinParted.Core.Operations;
using WinParted.Core.Presentation;
using WinParted.Core.Tests.Builders;
using static WinParted.Core.Tests.Builders.DiskBuilder;

namespace WinParted.Core.Tests.Presentation;

/// <summary>Dialog defaults, validation and requests (tasks 12.2-12.4, mvp-features.md section 6).</summary>
public class DialogModelTests
{
    public DialogModelTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    }

    [Fact]
    public void Initialize_dialog()
    {
        var sd = Layouts.Uninitialized(3);
        var model = new InitializeDialogModel(Layouts.Snapshot(sd), sd);

        Assert.Equal("Initialize Disk 3?", model.Title);
        Assert.Equal("Generic SD/MMC · 29.0 GB · SD", model.IdentityText);
        Assert.Null(model.MbrWarning);
        Assert.StartsWith("Windows adds a 16 MB Microsoft Reserved", InitializeDialogModel.ResultHint(PartitionStyle.Gpt), StringComparison.Ordinal);

        var request = Assert.IsType<InitializeDiskRequest>(model.Request(PartitionStyle.Mbr));
        Assert.Equal(PartitionStyle.Mbr, request.Style);
    }

    [Fact]
    public void MBR_on_a_disk_over_2_TB_warns()
    {
        // spec disk-operations "MBR on a disk larger than 2 TB"
        var big = new DiskBuilder(5, PartitionStyle.Uninitialized).Build(totalSize: 4 * TB);
        var model = new InitializeDialogModel(Layouts.Snapshot(big), big);

        Assert.Equal("With MBR, only the first 2 TB of this 4.00 TB disk can be used.", model.MbrWarning);
    }

    [Fact]
    public void Clear_dialog_requires_the_exact_disk_number()
    {
        // spec disk-operations "Wrong number typed"
        var usb = Layouts.UsbStick(3);
        var model = new ClearDialogModel(Layouts.Snapshot(usb), usb);

        Assert.Equal("Clear Disk 3?", model.Title);
        Assert.True(model.IsDestructive);
        Assert.Equal("Type 3 to confirm:", model.ConfirmLabel);
        Assert.False(model.IsConfirmed("2"));
        Assert.False(model.IsConfirmed(""));
        Assert.False(model.IsConfirmed(null));
        Assert.True(model.IsConfirmed(" 3 "));
        Assert.True(model.EnterConfirms); // the typed number is the deliberate step
        Assert.Equal("BACKUP (E:)", Assert.Single(model.Segments).Title);
    }

    [Fact]
    public void Offline_dialog_names_the_volumes()
    {
        var usb = Layouts.UsbStick(2);
        var model = new OfflineDialogModel(Layouts.Snapshot(usb), usb);

        Assert.Equal("Take Disk 2 offline?", model.Title);
        Assert.StartsWith("Its volumes (E:) won't be available", model.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_dialog_defaults_and_size_validation()
    {
        var disk = Layouts.DataDiskWithLinux();
        var region = disk.UnallocatedSegments.Single();
        var snapshot = Layouts.Snapshot(disk);
        var model = new CreateDialogModel(snapshot, disk, region);

        Assert.Equal("Create partition on Disk 1", model.Title);
        Assert.Equal(SizeUnit.GB, model.DefaultUnit);
        Assert.Null(model.SizeError(SizeInput.FromBytes(model.MaxSize, SizeUnit.GB), SizeUnit.GB));
        Assert.Equal("The free space is 320 GB. Enter 320 GB or less.", model.SizeError(400, SizeUnit.GB));
        Assert.Equal("Enter at least 1 MB.", model.SizeError(0, SizeUnit.MB));

        var fs = model.FileSystems(320 * GB);
        Assert.Equal(["NTFS", "exFAT", "FAT32"], fs.Select(c => c.Text));
        Assert.False(fs.Single(c => c.Value == FileSystemKind.Fat32).IsEnabled);

        var letters = model.Letters(snapshot.Facts);
        Assert.Equal("C:", letters[0].Text);
        Assert.Equal("No drive letter", letters[^1].Text);
    }

    [Fact]
    public void Size_limits_are_exact()
    {
        // spec partition-operations "Size limits are exact"
        Assert.Equal("14.43 GB", SizeInput.Precise((ulong)(14.435 * GB)));
        Assert.Equal("300 MB", SizeInput.Precise(300 * MB));

        var disk = new DiskBuilder(1, PartitionStyle.Mbr).Free((ulong)(14.435 * GB)).Build();
        var model = new CreateDialogModel(Layouts.Snapshot(disk), disk, disk.UnallocatedSegments.Single());
        Assert.Equal("1 MB to 14.43 GB", model.RangeText);
        Assert.Equal("The free space is 14.43 GB. Enter 14.43 GB or less.", model.SizeError(model.MaxSize + GB));
        Assert.Null(model.SizeError(model.MaxSize));
    }

    [Fact]
    public void Create_without_format_has_no_letter()
    {
        var disk = Layouts.DataDiskWithLinux();
        var model = new CreateDialogModel(Layouts.Snapshot(disk), disk, disk.UnallocatedSegments.Single());

        var request = Assert.IsType<CreatePartitionRequest>(model.Request(10 * GB, format: null, letter: 'F'));
        Assert.Null(request.Format);
        Assert.Null(request.DriveLetter);
    }

    [Fact]
    public void Delete_dialog_texts()
    {
        var usb = Layouts.UsbStick(2);
        var model = new DeleteDialogModel(Layouts.Snapshot(usb), usb.Partitions[0], usb);

        Assert.Equal("Delete BACKUP (E:)?", model.Title);
        Assert.False(model.EnterConfirms); // nothing to type, so Enter means Cancel
        Assert.Equal("All files on BACKUP (E:) on Disk 2 will be permanently erased (41.0 GB used). The space will become unallocated.", model.Body);
        Assert.Equal("Programs using BACKUP (E:) will lose access to it and may lose unsaved work. Close them first.", model.Note);

        var linuxDisk = Layouts.DataDiskWithLinux();
        var linux = new DeleteDialogModel(Layouts.Snapshot(linuxDisk), linuxDisk.Partitions[1], linuxDisk);
        Assert.Equal("Delete Unknown filesystem partition on Disk 1?", linux.Title);
        Assert.Contains("dual-boot", linux.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_dialog_defaults()
    {
        var disk = Layouts.DataDiskWithLinux();
        var data = disk.Partitions[0];
        var model = new FormatDialogModel(Layouts.Snapshot(disk), data, disk);

        Assert.Equal("Format Data (D:)?", model.Title);
        Assert.Equal("All files on Data (D:) will be erased (740 GB used).", model.Body);
        Assert.Equal(FileSystemKind.Ntfs, model.DefaultFileSystem);
        Assert.Equal("Data", model.DefaultLabel);
        Assert.Equal("The drive letter D: is kept.", model.KeepsLetterText);
        Assert.Null(FormatChoices.FullFormatReason(FileSystemKind.Ntfs, model.Bus));
        Assert.NotNull(FormatChoices.FullFormatReason(FileSystemKind.Ntfs, BusType.Virtual));
    }

    [Fact]
    public void Shrink_dialog_validation_and_explanation()
    {
        var disk = Layouts.Windows11Default();
        var c = disk.Partitions[2];
        var model = new ShrinkDialogModel(Layouts.Snapshot(disk), c);
        var minimum = 520 * GB;

        Assert.Equal("Shrink C:", model.Title);
        Assert.Null(model.SizeError(730, SizeUnit.GB, minimum));
        Assert.Equal("Enter a size between 520 GB and 829.99 GB.", model.SizeError(400, SizeUnit.GB, minimum));
        Assert.Equal("frees 100 GB", model.FreesText(730 * GB));
        Assert.Contains("can't be made smaller than 520 GB", model.MinimumExplanation(minimum), StringComparison.Ordinal);
        Assert.Null(model.MinimumExplanation(413 * GB));
        Assert.Equal(730 * GB, Assert.IsType<ResizePartitionRequest>(model.Request(730 * GB)).NewSize);
    }

    [Fact]
    public void Extend_dialog_uses_free_space_after()
    {
        var disk = new DiskBuilder(1).Vol(100 * GB, label: "Data", letter: 'D').Free(50 * GB).Build();
        var model = new ExtendDialogModel(Layouts.Snapshot(disk), disk.Partitions[0], disk);

        Assert.InRange(model.FreeAfter, 50 * GB, 51 * GB);
        Assert.Null(model.SizeError(150, SizeUnit.GB, model.LayoutMax));
        Assert.NotNull(model.SizeError(200, SizeUnit.GB, model.LayoutMax));
        Assert.False(Assert.IsType<ResizePartitionRequest>(model.Request(150 * GB)).IsShrink);
    }

    [Fact]
    public void Letter_dialog_warnings()
    {
        var usb = Layouts.UsbStick(2);
        var model = new LetterDialogModel(Layouts.Snapshot(usb), usb.Partitions[0]);

        Assert.Equal("E: (current)", model.Letters[0].Text);
        Assert.Equal('C', model.DefaultChoice.Value);
        Assert.Equal("Programs and shortcuts that use E: may need to be updated.", model.Warning('F'));
        Assert.Equal("Programs and shortcuts that use E: may stop working. The files on the volume are not affected.", model.Warning(null));
        Assert.Equal("Remove letter", model.PrimaryTextFor(null));
        Assert.False(model.CanApply('E'));
    }

    [Fact]
    public void Label_dialog_validation()
    {
        var disk = new DiskBuilder(1).Vol(16 * GB, label: "CAMERA", letter: 'H', fs: FileSystemKind.Fat32).Build();
        var model = new LabelDialogModel(Layouts.Snapshot(disk), disk.Partitions[0]);

        Assert.Equal("FAT32 labels can be at most 11 characters.", model.Error("Holiday 2026"));
        Assert.Equal("FAT32 · 12 of 11 characters", model.CountText("Holiday 2026"));
        Assert.False(model.CanApply("CAMERA"));
        Assert.True(model.CanApply("TRIP"));
    }

    [Fact]
    public void Dialog_detects_a_changed_target()
    {
        // spec device-change-detection "Disk changes while confirm dialog is open"
        var usb = Layouts.UsbStick(3);
        var model = new ClearDialogModel(Layouts.Snapshot(usb), usb);

        Assert.False(model.IsStale(Layouts.Snapshot(usb)));
        Assert.True(model.IsStale(Layouts.Snapshot(Layouts.Windows11Default())));
    }

    [Fact]
    public void Operation_texts()
    {
        var disk = Layouts.DataDiskWithLinux();
        var snapshot = Layouts.Snapshot(disk);
        var format = new FormatDialogModel(snapshot, disk.Partitions[0], disk).Request(new FormatOptions(FileSystemKind.ExFat, null));

        Assert.Equal("Formatting Data (D:)", OperationTexts.Busy(snapshot, format));
        Assert.Equal("Data (D:) was formatted as exFAT.", OperationTexts.Done(snapshot, format));
        Assert.Equal("Formatting Data (D:) failed", OperationTexts.FailedTitle(snapshot, format));

        var clear = new ClearDialogModel(snapshot, disk).Request();
        Assert.Equal("Clearing Disk 1", OperationTexts.Busy(snapshot, clear));
        Assert.Equal("Disk 1 was cleared. It's empty.", OperationTexts.Done(snapshot, clear));
    }
}
