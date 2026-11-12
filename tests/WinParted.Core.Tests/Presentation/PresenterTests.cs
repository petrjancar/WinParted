using System.Globalization;
using WinParted.Core.Actions;
using WinParted.Core.Model;
using WinParted.Core.Presentation;
using WinParted.Core.Selections;
using WinParted.Core.Tests.Builders;
using static WinParted.Core.Tests.Builders.DiskBuilder;

namespace WinParted.Core.Tests.Presentation;

public class PresenterTests
{
    public PresenterTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    }

    [Fact]
    public void Windows11_segments_match_the_design()
    {
        var segments = Presenter.Segments(Layouts.Windows11Default());

        Assert.Equal(["EFI System", "Microsoft Reserved", "C:", "Recovery", "Unallocated"], segments.Select(s => s.Title));
        Assert.Equal(["EFI", "MSR", "C:", "Rec.", "Unallocated"], segments.Select(s => s.ShortTitle));
        Assert.Equal(
            [SegmentKind.Protected, SegmentKind.Protected, SegmentKind.Volume, SegmentKind.Protected, SegmentKind.Unallocated],
            segments.Select(s => s.Kind));

        var c = segments[2];
        Assert.True(c.IsLocked);
        Assert.Equal("NTFS · 830 GB", c.Line2);
        Assert.Equal("412 GB used · 50%", c.Line3);
        Assert.Equal("Windows is installed here", c.Role);
        Assert.Contains("protected", c.AutomationName, StringComparison.Ordinal);

        Assert.Equal("FAT32 · 100 MB", segments[0].Line2);
        Assert.Equal("Protected", segments[0].Line3);
        Assert.Equal("Not partitioned", segments[4].Line3);
    }

    [Fact]
    public void Unknown_and_unformatted_segments()
    {
        var linux = Presenter.Segments(Layouts.DataDiskWithLinux())[1];
        Assert.Equal(SegmentKind.Unknown, linux.Kind);
        Assert.Equal("Unknown · 300 GB", linux.Line2);
        Assert.Equal("Contents unreadable", linux.Line3);

        var raw = Presenter.Segments(new DiskBuilder(1).Part(50 * GB).Build())[0];
        Assert.Equal(SegmentKind.Unformatted, raw.Kind);
        Assert.Equal("RAW · 50.0 GB", raw.Line2);
        Assert.Equal("No file system", raw.Line3);
    }

    [Fact]
    public void BitLocker_segments()
    {
        var locked = Presenter.Segments(Layouts.BitLocker(BitLockerState.Locked))[0];
        Assert.Equal(SegmentKind.BitLockerLocked, locked.Kind);
        Assert.Equal("Locked by BitLocker", locked.Line3);

        var unlocked = Presenter.Segments(Layouts.BitLocker(BitLockerState.Unlocked))[0];
        Assert.Equal(SegmentKind.Volume, unlocked.Kind);
        Assert.True(unlocked.IsEncrypted);
        Assert.StartsWith("Encrypted · NTFS", unlocked.Line2, StringComparison.Ordinal);
    }

    [Fact]
    public void Disk_entries_carry_status_chips()
    {
        var system = Presenter.Disk(Layouts.Windows11Default());
        Assert.Equal("Disk 0", system.Title);
        Assert.Equal("NVMe · GPT", system.Meta);
        Assert.Contains(system.Chips, c => c.Text == "System disk");
        Assert.Equal(DiskState.Normal, system.State);

        var sd = Presenter.Disk(Layouts.Uninitialized());
        Assert.Equal(DiskState.NotInitialized, sd.State);
        Assert.Contains(sd.Chips, c => c.Text == "Not initialized");

        var offline = Presenter.Disk(new DiskBuilder(4) { IsOffline = true }.Build());
        Assert.Equal(DiskState.Offline, offline.State);
        Assert.Contains(offline.Chips, c => c.Text == "Offline" && c.Tone == ChipTone.Warning);

        var busy = Presenter.Disk(Layouts.UsbStick(), isBusy: true);
        Assert.Contains(busy.Chips, c => c.Text == "Busy");
    }

    [Fact]
    public void Partition_details()
    {
        var disk = Layouts.Windows11Default();
        var c = disk.Partitions[2];
        var details = Presenter.Details(Layouts.Snapshot(disk), Selection.OfPartition(c.Id))!;

        Assert.Equal("C:", details.Title);
        Assert.True(details.IsLocked);
        Assert.True(details.HasUsage);
        Assert.Equal("50%", details.PercentText);
        Assert.Contains(details.Rows, r => r.Label == "Type" && r.Value == "Basic data (GPT)");
        Assert.Contains(details.Rows, r => r.Label == "Drive letter" && r.Value == "C:");
        Assert.Contains("Boot", details.Flags);
    }

    [Fact]
    public void Unallocated_and_disk_details()
    {
        var disk = Layouts.Windows11Default();
        var snapshot = Layouts.Snapshot(disk);
        var free = disk.UnallocatedSegments.Single();

        var u = Presenter.Details(snapshot, Selection.OfUnallocated(disk.Id, free.Offset))!;
        Assert.Equal("Unallocated", u.Title);
        Assert.Equal("You can create a new partition here.", u.Hint);

        var d = Presenter.Details(snapshot, Selection.OfDisk(disk.Id))!;
        Assert.True(d.IsDisk);
        Assert.Equal("System disk - Windows is installed on this disk", d.Role);
        Assert.Contains(d.Rows, r => r.Label == "Bus type" && r.Value == "NVMe");
    }

    [Fact]
    public void Partition_actions_are_grouped_with_reasons()
    {
        var disk = Layouts.Windows11Default();
        var actions = Presenter.Actions(Layouts.Snapshot(disk), Selection.OfPartition(disk.Partitions[2].Id), isBusy: false)!;

        Assert.Equal(["Contents", "Size", "Name and drive letter"], actions.Groups.Select(g => g.Name));
        var extend = actions.Groups[1].Items.Single(i => i.Kind == ActionKind.ExtendVolume);
        Assert.False(extend.IsEnabled);
        Assert.Equal("C: can only grow into free space directly after it. The Recovery partition is in the way.", extend.Reason);
        Assert.True(actions.Groups[1].Items.Single(i => i.Kind == ActionKind.ShrinkVolume).IsEnabled);
    }

    [Fact]
    public void Unallocated_offers_create_in_its_own_group()
    {
        var disk = Layouts.DataDiskWithLinux();
        var free = disk.UnallocatedSegments.Single();
        var actions = Presenter.Actions(Layouts.Snapshot(disk), Selection.OfUnallocated(disk.Id, free.Offset), false)!;

        var group = Assert.Single(actions.Groups);
        Assert.Equal("New partition", group.Name);
        Assert.Equal("Create partition", Assert.Single(group.Items).Label);
    }

    [Fact]
    public void Disk_actions_share_one_reason_line()
    {
        // Reasons board, example 2.
        var disk = Layouts.Windows11Default();
        var view = Presenter.DiskActions(Layouts.Snapshot(disk), disk.Id, false);

        Assert.Equal(["Initialize", "Take offline", "Clear disk"], view.Items.Select(i => i.Label));
        var summary = Assert.Single(view.Reasons);
        Assert.Equal("Initialize, Take offline and Clear disk: Windows is installed on this disk.", Presenter.ReasonSummaryText(summary));
    }

    [Fact]
    public void Join_and()
    {
        Assert.Equal("", Presenter.JoinAnd([]));
        Assert.Equal("A", Presenter.JoinAnd(["A"]));
        Assert.Equal("A and B", Presenter.JoinAnd(["A", "B"]));
        Assert.Equal("A, B and C", Presenter.JoinAnd(["A", "B", "C"]));
    }
}

public class SegmentWidthsTests
{
    [Fact]
    public void Proportional_when_nothing_is_tiny()
    {
        var (w, notToScale) = SegmentWidths.Compute([100, 300], [false, false], 404, 4);

        Assert.False(notToScale);
        Assert.Equal(100, w[0], 3);
        Assert.Equal(300, w[1], 3);
    }

    [Fact]
    public void Tiny_partitions_get_the_minimum_and_bar_is_marked_not_to_scale()
    {
        // Windows 11 layout: 100 MB EFI, 16 MB MSR, 830 GB, 750 MB, 100 GB free on an 888 px bar.
        var sizes = new ulong[] { 100 * MB, 16 * MB, 830 * GB, 750 * MB, 100 * GB };
        var (w, notToScale) = SegmentWidths.Compute(sizes, [false, false, false, false, true], 888, 4);

        Assert.True(notToScale);
        Assert.Equal(64, w[0]);
        Assert.Equal(64, w[1]);
        Assert.Equal(64, w[3]);
        Assert.True(w[4] >= 96);
        Assert.Equal(888 - (4 * 4), w.Sum(), 3);
    }

    [Fact]
    public void Empty_layout()
    {
        Assert.Empty(SegmentWidths.Compute([], [], 500, 4).Widths);
    }
}

public class ChangeMessagesTests
{
    [Fact]
    public void Connected_and_disconnected_disks()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        var system = Layouts.Windows11Default();
        var usb = Layouts.UsbStick(2);
        var sd = Layouts.Uninitialized(3);

        var plugged = ChangeMessages.Between(Layouts.Snapshot(system), Layouts.Snapshot(system, usb), null);
        Assert.Equal(["USB disk connected: SanDisk Ultra USB 3.0 (57.0 GB)"], plugged);

        var removed = ChangeMessages.Between(Layouts.Snapshot(system, usb, sd), Layouts.Snapshot(system), usb.Id, system.Id);
        Assert.Equal(["Disk 2 was disconnected. Showing Disk 0 instead.", "Disk 3 was disconnected"], removed);

        var lastDisk = ChangeMessages.Between(Layouts.Snapshot(usb), new StorageSnapshot(), usb.Id, null);
        Assert.Equal(["Disk 2 was disconnected"], lastDisk);
    }
}
