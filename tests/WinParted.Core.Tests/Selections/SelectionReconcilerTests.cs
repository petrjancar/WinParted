using WinParted.Core.Model;
using WinParted.Core.Selections;
using WinParted.Core.Tests.Builders;
using static WinParted.Core.Tests.Builders.DiskBuilder;

namespace WinParted.Core.Tests.Selections;

public class SelectionReconcilerTests
{
    [Fact]
    public void Refresh_keeps_selected_partition()
    {
        // spec disk-inventory "Refresh keeps selection"
        var b = new DiskBuilder(1).Vol(10 * GB, letter: 'D').Vol(10 * GB, letter: 'E');
        var before = b.Build();
        var partition2 = before.Partitions[1].Id;
        var after = Layouts.Snapshot(b.Build());

        var result = SelectionReconciler.Reconcile(Selection.OfPartition(partition2), after);

        Assert.Equal(Selection.OfPartition(partition2), result.Selection);
        Assert.False(result.SelectedDiskRemoved);
    }

    [Fact]
    public void Missing_partition_falls_back_to_its_disk()
    {
        // spec disk-inventory "Selected item gone after refresh"
        var disk = new DiskBuilder(1).Vol(10 * GB).Build();
        var gone = new PartitionId(disk.Id, 999 * GB);

        var result = SelectionReconciler.Reconcile(Selection.OfPartition(gone), Layouts.Snapshot(disk));

        Assert.Equal(Selection.OfDisk(disk.Id), result.Selection);
    }

    [Fact]
    public void Removed_disk_clears_selection_and_reports_it()
    {
        // spec device-change-detection "Selected disk unplugged"
        var usb = Layouts.UsbStick();
        var selection = Selection.OfPartition(usb.Partitions[0].Id);

        var result = SelectionReconciler.Reconcile(selection, Layouts.Snapshot(Layouts.Windows11Default()));

        Assert.Equal(Selection.None, result.Selection);
        Assert.True(result.SelectedDiskRemoved);
    }

    [Fact]
    public void Disk_selection_survives_number_change()
    {
        var disk = new Disk { Id = new DiskId("stable"), Number = 2 };
        var renumbered = new Disk { Id = new DiskId("stable"), Number = 3 };

        var result = SelectionReconciler.Reconcile(Selection.OfDisk(disk.Id), new StorageSnapshot { Disks = [renumbered] });

        Assert.Equal(Selection.OfDisk(disk.Id), result.Selection);
    }

    [Fact]
    public void Unallocated_selection_follows_the_region_containing_it()
    {
        var b = new DiskBuilder(1).Vol(10 * GB).Free(50 * GB);
        var before = b.Build();
        var region = before.UnallocatedSegments.Single();

        // A partition was created at the start of the region; the rest stays free.
        var after = new DiskBuilder(1).Vol(10 * GB).Vol(20 * GB).Free(30 * GB).Build();
        var snapshot = new StorageSnapshot { Disks = [WithId(after, before.Id)] };

        var result = SelectionReconciler.Reconcile(Selection.OfUnallocated(before.Id, region.Offset + (25 * GB)), snapshot);

        var remaining = snapshot.Disks[0].UnallocatedSegments.Single();
        Assert.Equal(Selection.OfUnallocated(before.Id, remaining.Offset), result.Selection);
    }

    [Fact]
    public void Unallocated_selection_falls_back_to_disk_when_region_is_gone()
    {
        var before = new DiskBuilder(1).Vol(10 * GB).Free(50 * GB).Build();
        var region = before.UnallocatedSegments.Single();
        var after = WithId(new DiskBuilder(1).Vol(10 * GB).Vol(50 * GB).Build(), before.Id);

        var result = SelectionReconciler.Reconcile(
            Selection.OfUnallocated(before.Id, region.Offset), new StorageSnapshot { Disks = [after] });

        Assert.Equal(Selection.OfDisk(before.Id), result.Selection);
    }

    private static Disk WithId(Disk d, DiskId id) => new()
    {
        Id = id,
        Number = d.Number,
        Size = d.Size,
        Style = d.Style,
        Partitions = [.. d.Partitions.Select(p => p with { Id = new PartitionId(id, p.Offset) })],
    };
}
