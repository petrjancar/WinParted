using WinParted.Core.Actions;
using WinParted.Core.Model;
using WinParted.Core.Tests.Builders;
using static WinParted.Core.Tests.Actions.AvailabilityAssert;
using static WinParted.Core.Tests.Builders.DiskBuilder;

namespace WinParted.Core.Tests.Actions;

/// <summary>One test per disabled scenario in partition-operations and disk-operations (task 6.2).</summary>
public class PreconditionTests
{
    // ----- Extend ------------------------------------------------------------------------------

    [Fact]
    public void Extend_blocked_by_following_partition()
    {
        // spec operation-execution "Extend blocked by following partition"
        var disk = Layouts.Windows11Default();
        var state = Evaluate(Layouts.Snapshot(disk), ActionKind.ExtendVolume, disk.Partitions[2]);

        Disabled(state, ReasonCode.ExtendBlockedByPartition);
        Assert.Equal(
            "C: can only grow into free space directly after it. The Recovery partition is in the way.",
            state.DisabledReason!.Text);
    }

    [Fact]
    public void Extend_into_following_free_space()
    {
        // spec partition-operations "Extend into following free space"
        var disk = new DiskBuilder(1).Vol(100 * GB, label: "Data", letter: 'D').Free(50 * GB).Build();
        Enabled(Evaluate(Layouts.Snapshot(disk), ActionKind.ExtendVolume, disk.Partitions[0]));
    }

    [Fact]
    public void Free_space_before_the_volume()
    {
        // spec partition-operations "Free space before the volume"
        var disk = new DiskBuilder(1).Free(50 * GB).Vol(100 * GB, label: "Data", letter: 'D').Build();
        var state = Evaluate(Layouts.Snapshot(disk), ActionKind.ExtendVolume, disk.Partitions[0]);

        Disabled(state, ReasonCode.ExtendFreeSpaceOnlyBefore);
        Assert.Contains("Moving partitions isn't supported", state.DisabledReason!.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Extend_with_no_free_space_anywhere()
    {
        var disk = new DiskBuilder(1).Vol(100 * GB, label: "Data", letter: 'D').Vol(100 * GB, letter: 'E').Build();
        var state = Evaluate(Layouts.Snapshot(disk), ActionKind.ExtendVolume, disk.Partitions[0]);

        Disabled(state, ReasonCode.ExtendNoFreeSpace);
        Assert.Equal("There's no unallocated space after D:.", state.DisabledReason!.Text);
    }

    [Fact]
    public void Extend_reason_uses_drive_letter_even_when_labelled()
    {
        // As in the design: "D: can only grow…" for "Data (D:)", and the blocker by its type name.
        var disk = Layouts.DataDiskWithLinux();
        var state = Evaluate(Layouts.Snapshot(disk), ActionKind.ExtendVolume, disk.Partitions[0]);

        Assert.Equal(
            "D: can only grow into free space directly after it. The Unknown filesystem partition is in the way.",
            state.DisabledReason!.Text);
    }

    [Fact]
    public void Extend_reason_uses_title_when_no_drive_letter()
    {
        var disk = new DiskBuilder(1).Vol(100 * GB, label: "Archive").Vol(100 * GB, letter: 'E').Build();
        var state = Evaluate(Layouts.Snapshot(disk), ActionKind.ExtendVolume, disk.Partitions[0]);

        Assert.Equal("There's no unallocated space after Archive.", state.DisabledReason!.Text);
    }

    [Fact]
    public void Extend_blocked_names_data_partitions_by_title()
    {
        var disk = new DiskBuilder(1).Vol(100 * GB, letter: 'D').Vol(100 * GB, label: "Photos", letter: 'E').Free(10 * GB).Build();
        var state = Evaluate(Layouts.Snapshot(disk), ActionKind.ExtendVolume, disk.Partitions[0]);

        Assert.Equal(
            "D: can only grow into free space directly after it. The Photos (E:) partition is in the way.",
            state.DisabledReason!.Text);
    }

    // ----- Resize preconditions ------------------------------------------------------------------

    [Fact]
    public void Shrink_on_unsupported_filesystem()
    {
        // spec operation-execution "Shrink on unsupported filesystem"
        var disk = new DiskBuilder(1).Vol(16 * GB, letter: 'F', fs: FileSystemKind.Fat32).Free(10 * GB).Build();
        var snapshot = Layouts.Snapshot(disk);

        var shrink = Evaluate(snapshot, ActionKind.ShrinkVolume, disk.Partitions[0]);
        Disabled(shrink, ReasonCode.ResizeUnsupportedFileSystem);
        Assert.Equal("Only NTFS and ReFS volumes can be resized. This volume is FAT32.", shrink.DisabledReason!.Text);
        Disabled(Evaluate(snapshot, ActionKind.ExtendVolume, disk.Partitions[0]), ReasonCode.ResizeUnsupportedFileSystem);
    }

    [Fact]
    public void Resize_unformatted_partition_needs_format()
    {
        var disk = new DiskBuilder(1).Part(10 * GB).Free(10 * GB).Build();
        Disabled(Evaluate(Layouts.Snapshot(disk), ActionKind.ShrinkVolume, disk.Partitions[0]), ReasonCode.ResizeNeedsFormat);
    }

    [Fact]
    public void Shrink_at_minimum()
    {
        var disk = new DiskBuilder(1).Vol(100 * GB, letter: 'D', partition: p => p with { MinimumSize = 100 * GB }).Build();
        Disabled(Evaluate(Layouts.Snapshot(disk), ActionKind.ShrinkVolume, disk.Partitions[0]), ReasonCode.ShrinkAtMinimum);
    }

    [Fact]
    public void Shrink_enabled_when_minimum_unknown()
    {
        var disk = new DiskBuilder(1).Vol(100 * GB, letter: 'D').Build();
        Enabled(Evaluate(Layouts.Snapshot(disk), ActionKind.ShrinkVolume, disk.Partitions[0]));
    }

    // ----- Unreadable / unformatted --------------------------------------------------------------

    [Fact]
    public void Linux_partition_can_only_be_deleted()
    {
        // spec partition-operations "Delete a Linux partition"
        var disk = Layouts.DataDiskWithLinux();
        var linux = disk.Partitions[1];
        var snapshot = Layouts.Snapshot(disk);

        Enabled(Evaluate(snapshot, ActionKind.DeletePartition, linux));
        foreach (var kind in new[] { ActionKind.FormatVolume, ActionKind.ShrinkVolume, ActionKind.ExtendVolume, ActionKind.ChangeDriveLetter, ActionKind.ChangeLabel })
        {
            Disabled(Evaluate(snapshot, kind, linux), ReasonCode.UnreadablePartition);
        }
    }

    [Fact]
    public void Unformatted_partition_can_be_formatted_but_not_labelled()
    {
        var disk = new DiskBuilder(1).Part(10 * GB).Build();
        var snapshot = Layouts.Snapshot(disk);

        Enabled(Evaluate(snapshot, ActionKind.FormatVolume, disk.Partitions[0]));
        Disabled(Evaluate(snapshot, ActionKind.ChangeLabel, disk.Partitions[0]), ReasonCode.NeedsFormatFirst);
        Disabled(Evaluate(snapshot, ActionKind.ChangeDriveLetter, disk.Partitions[0]), ReasonCode.NeedsFormatFirst);
    }

    // ----- Drive letters -------------------------------------------------------------------------

    [Fact]
    public void All_drive_letters_in_use_blocks_assigning_but_not_changing()
    {
        var disk = new DiskBuilder(1).Vol(10 * GB).Vol(10 * GB, letter: 'Z').Build();
        var snapshot = new StorageSnapshot
        {
            Disks = [disk],
            Facts = new SystemFacts { UsedDriveLetters = [.. Enumerable.Range('C', 24).Select(c => (char)c)] },
        };

        Disabled(Evaluate(snapshot, ActionKind.ChangeDriveLetter, disk.Partitions[0]), ReasonCode.AllDriveLettersInUse);
        Enabled(Evaluate(snapshot, ActionKind.ChangeDriveLetter, disk.Partitions[1]));
    }

    // ----- Create (MBR limit) --------------------------------------------------------------------

    [Fact]
    public void MBR_partition_limit_reached()
    {
        // spec partition-operations "MBR partition limit reached"
        var disk = new DiskBuilder(1, PartitionStyle.Mbr).Vol(GB).Vol(GB).Vol(GB).Vol(GB).Free(10 * GB).Build();
        var free = disk.UnallocatedSegments.Single();

        var state = ActionAvailability.Evaluate(Layouts.Snapshot(disk), ActionKind.CreatePartition, new UnallocatedTarget(disk.Id, free.Offset), false);

        Disabled(state, ReasonCode.MbrPartitionLimit);
    }

    [Fact]
    public void MBR_with_three_partitions_can_create()
    {
        var disk = new DiskBuilder(1, PartitionStyle.Mbr).Vol(GB).Vol(GB).Vol(GB).Free(10 * GB).Build();
        var free = disk.UnallocatedSegments.Single();

        Enabled(ActionAvailability.Evaluate(Layouts.Snapshot(disk), ActionKind.CreatePartition, new UnallocatedTarget(disk.Id, free.Offset), false));
    }

    [Fact]
    public void GPT_has_no_four_partition_limit()
    {
        var disk = new DiskBuilder(1).Vol(GB).Vol(GB).Vol(GB).Vol(GB).Vol(GB).Free(10 * GB).Build();
        var free = disk.UnallocatedSegments.Single();

        Enabled(ActionAvailability.Evaluate(Layouts.Snapshot(disk), ActionKind.CreatePartition, new UnallocatedTarget(disk.Id, free.Offset), false));
    }

    // ----- Disk operations -----------------------------------------------------------------------

    [Fact]
    public void Initialize_not_offered_for_initialized_disk()
    {
        // spec disk-operations "Initialize not offered for initialized disk"
        var disk = Layouts.UsbStick();
        var state = Evaluate(Layouts.Snapshot(disk), ActionKind.Initialize, disk);

        Disabled(state, ReasonCode.AlreadyInitialized);
        Assert.Contains("Clear disk", state.DisabledReason!.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Initialize_enabled_for_new_disk()
    {
        var disk = Layouts.Uninitialized();
        Enabled(Evaluate(Layouts.Snapshot(disk), ActionKind.Initialize, disk));
    }

    [Fact]
    public void Clear_on_uninitialized_disk_is_pointless()
    {
        var disk = Layouts.Uninitialized();
        Disabled(Evaluate(Layouts.Snapshot(disk), ActionKind.ClearDisk, disk), ReasonCode.DiskAlreadyEmpty);
    }

    [Fact]
    public void Removable_media_cant_go_offline()
    {
        // spec disk-operations "Removable media can't go offline"
        var usb = Layouts.UsbStick();
        Disabled(Evaluate(Layouts.Snapshot(usb), ActionKind.TakeOffline, usb), ReasonCode.RemovableMediaOffline);

        var external = new DiskBuilder(4) { Bus = BusType.Usb }.Vol(GB).Build();
        Enabled(Evaluate(Layouts.Snapshot(external), ActionKind.TakeOffline, external));
    }

    [Fact]
    public void Clear_usb_stick_is_allowed()
    {
        var disk = Layouts.UsbStick();
        Enabled(Evaluate(Layouts.Snapshot(disk), ActionKind.ClearDisk, disk));
    }
}

/// <summary>File system options and label validation (task 6.2).</summary>
public class FileSystemRulesTests
{
    [Fact]
    public void FAT32_on_a_64_GB_partition()
    {
        // spec partition-operations "FAT32 on a 64 GB partition"
        var options = FileSystemRules.FormatOptions(64 * GB);
        var fat32 = options.Single(o => o.FileSystem == FileSystemKind.Fat32);

        Assert.Equal(ReasonCode.Fat32TooLarge, fat32.DisabledReason?.Code);
        Assert.Equal("WinParted formats FAT32 up to 32 GB, like Windows does. Use exFAT for larger drives.", fat32.DisabledReason!.Text);
        Assert.True(options.Single(o => o.FileSystem == FileSystemKind.ExFat).IsEnabled);
    }

    [Fact]
    public void FAT32_allowed_up_to_32_GB()
    {
        Assert.True(FileSystemRules.FormatOptions(32 * GB).Single(o => o.FileSystem == FileSystemKind.Fat32).IsEnabled);
    }

    [Fact]
    public void NTFS_is_first_and_always_available()
    {
        var first = FileSystemRules.FormatOptions(4 * TB).First();
        Assert.Equal(FileSystemKind.Ntfs, first.FileSystem);
        Assert.True(first.IsEnabled);
    }

    [Fact]
    public void No_ReFS_option()
    {
        // spec partition-operations "No ReFS option"
        Assert.Equal(
            [FileSystemKind.Ntfs, FileSystemKind.ExFat, FileSystemKind.Fat32],
            FileSystemRules.FormatOptions(100 * GB).Select(o => o.FileSystem));
    }

    [Theory]
    [InlineData(FileSystemKind.Ntfs, BusType.Usb, null)]
    [InlineData(FileSystemKind.Ntfs, BusType.Nvme, null)]
    [InlineData(FileSystemKind.ExFat, BusType.Virtual, null)]
    [InlineData(FileSystemKind.Fat32, BusType.Virtual, null)]
    [InlineData(FileSystemKind.Ntfs, BusType.Virtual, ReasonCode.FullFormatNotOnVirtualDisk)]
    public void Full_format_available_except_NTFS_on_virtual_disks(FileSystemKind fs, BusType bus, ReasonCode? expected)
    {
        // spec partition-operations "Quick and full for every file system", "Full NTFS on a virtual disk"
        Assert.Equal(expected, FileSystemRules.FullFormatReason(fs, bus)?.Code);
    }

    [Fact]
    public void Label_too_long_for_FAT32()
    {
        // spec partition-operations "Label too long for FAT32"
        var reason = FileSystemRules.ValidateLabel(FileSystemKind.Fat32, "FIFTEEN_CHARSXX");

        Assert.Equal(ReasonCode.LabelTooLong, reason?.Code);
        Assert.Equal("FAT32 labels can be at most 11 characters.", reason!.Text);
    }

    [Theory]
    [InlineData(FileSystemKind.Ntfs, 32)]
    [InlineData(FileSystemKind.ReFS, 32)]
    [InlineData(FileSystemKind.ExFat, 15)]
    [InlineData(FileSystemKind.Fat32, 11)]
    public void Label_length_limits(FileSystemKind fs, int max)
    {
        Assert.Null(FileSystemRules.ValidateLabel(fs, new string('A', max)));
        Assert.Equal(ReasonCode.LabelTooLong, FileSystemRules.ValidateLabel(fs, new string('A', max + 1))?.Code);
    }

    [Fact]
    public void FAT32_rejects_special_characters()
    {
        var reason = FileSystemRules.ValidateLabel(FileSystemKind.Fat32, "A:B");
        Assert.Equal(ReasonCode.LabelInvalidCharacters, reason?.Code);
        Assert.Equal("FAT32 labels can't contain * ? . , ; : / \\ | + = < > [ ] or quotes.", reason!.Text);
        Assert.Equal(ReasonCode.LabelInvalidCharacters, FileSystemRules.ValidateLabel(FileSystemKind.Fat32, "A\"B")?.Code);
        Assert.Null(FileSystemRules.ValidateLabel(FileSystemKind.Ntfs, "Projects 2026"));
    }
}
