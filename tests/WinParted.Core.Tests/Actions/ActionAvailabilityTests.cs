using WinParted.Core.Actions;
using WinParted.Core.Model;
using WinParted.Core.Selections;
using WinParted.Core.Tests.Builders;
using static WinParted.Core.Tests.Actions.AvailabilityAssert;
using static WinParted.Core.Tests.Builders.DiskBuilder;

namespace WinParted.Core.Tests.Actions;

/// <summary>Action sets per selection type and rule ordering (task 6.1).</summary>
public class ActionAvailabilityTests
{
    [Fact]
    public void Disk_selection_offers_disk_actions()
    {
        var disk = Layouts.UsbStick();
        var states = ActionAvailability.ForSelection(Layouts.Snapshot(disk), Selection.OfDisk(disk.Id), false);

        Assert.Equal([ActionKind.Initialize, ActionKind.TakeOffline, ActionKind.ClearDisk], states.Select(s => s.Kind));
    }

    [Fact]
    public void Offline_disk_offers_bring_online_instead_of_take_offline()
    {
        var disk = new DiskBuilder(4) { IsOffline = true }.Vol(10 * GB).Build();
        var states = ActionAvailability.ForSelection(Layouts.Snapshot(disk), Selection.OfDisk(disk.Id), false);

        Assert.Contains(states, s => s.Kind == ActionKind.BringOnline && s.IsEnabled);
        Assert.DoesNotContain(states, s => s.Kind == ActionKind.TakeOffline);
    }

    [Fact]
    public void Unallocated_selection_offers_only_create()
    {
        // spec operation-execution "Unallocated region selected"
        var disk = Layouts.DataDiskWithLinux();
        var free = disk.UnallocatedSegments.Single();
        var states = ActionAvailability.ForSelection(Layouts.Snapshot(disk), Selection.OfUnallocated(disk.Id, free.Offset), false);

        var state = Assert.Single(states);
        Assert.Equal(ActionKind.CreatePartition, state.Kind);
        Assert.True(state.IsEnabled);
    }

    [Fact]
    public void Partition_selection_offers_partition_actions_even_when_disabled()
    {
        var disk = Layouts.Windows11Default();
        var states = ActionAvailability.ForSelection(Layouts.Snapshot(disk), Selection.OfPartition(disk.Partitions[3].Id), false);

        Assert.Equal(6, states.Length);
        Assert.All(states, s => Assert.False(s.IsEnabled));
    }

    [Fact]
    public void Nothing_selected_offers_nothing()
    {
        Assert.Empty(ActionAvailability.ForSelection(Layouts.Snapshot(Layouts.UsbStick()), Selection.None, false));
    }

    [Fact]
    public void Busy_disables_everything_first()
    {
        // spec operation-execution "Second operation blocked"
        var disk = Layouts.UsbStick();
        var snapshot = Layouts.Snapshot(disk);

        Disabled(Evaluate(snapshot, ActionKind.FormatVolume, disk.Partitions[0], isBusy: true), ReasonCode.OperationInProgress);
        Disabled(Evaluate(snapshot, ActionKind.ClearDisk, disk, isBusy: true), ReasonCode.OperationInProgress);
    }

    [Fact]
    public void Busy_wins_over_protection()
    {
        var disk = Layouts.Windows11Default();
        Disabled(Evaluate(Layouts.Snapshot(disk), ActionKind.DeletePartition, disk.Partitions[0], isBusy: true), ReasonCode.OperationInProgress);
    }

    [Fact]
    public void Read_only_wins_over_partition_protection()
    {
        var disk = new DiskBuilder(1) { IsReadOnly = true }
            .Vol(GB, letter: 'C', volume: v => v with { IsWindowsVolume = true }).Build();
        Disabled(Evaluate(Layouts.Snapshot(disk), ActionKind.DeletePartition, disk.Partitions[0]), ReasonCode.DiskReadOnly);
    }

    [Fact]
    public void Vanished_target_reports_target_changed()
    {
        var gone = Layouts.UsbStick();
        var snapshot = Layouts.Snapshot(Layouts.Windows11Default());

        Disabled(Evaluate(snapshot, ActionKind.ClearDisk, gone), ReasonCode.TargetChanged);
        Disabled(Evaluate(snapshot, ActionKind.FormatVolume, gone.Partitions[0]), ReasonCode.TargetChanged);
        Disabled(
            ActionAvailability.Evaluate(snapshot, ActionKind.CreatePartition, new UnallocatedTarget(gone.Id, MB), false),
            ReasonCode.TargetChanged);
    }

    [Fact]
    public void Plain_data_volume_allows_everything_applicable()
    {
        var disk = new DiskBuilder(1).Vol(100 * GB, label: "Data", letter: 'D', used: 10 * GB, partition: p => p with { MinimumSize = 20 * GB })
            .Free(50 * GB).Build();
        var states = ActionAvailability.ForSelection(Layouts.Snapshot(disk), Selection.OfPartition(disk.Partitions[0].Id), false);

        Assert.All(states, s => Assert.True(s.IsEnabled, $"{s.Kind}: {s.DisabledReason?.Code}"));
    }

    [Fact]
    public void Offline_disk_blocks_initialize_and_clear()
    {
        var disk = new DiskBuilder(4) { IsOffline = true }.Vol(10 * GB).Build();
        var snapshot = Layouts.Snapshot(disk);

        Disabled(Evaluate(snapshot, ActionKind.Initialize, disk), ReasonCode.DiskOffline);
        Disabled(Evaluate(snapshot, ActionKind.ClearDisk, disk), ReasonCode.DiskOffline);
        Disabled(Evaluate(snapshot, ActionKind.FormatVolume, disk.Partitions[0]), ReasonCode.DiskOffline);
    }

    [Fact]
    public void Actions_on_wrong_target_type_throw()
    {
        var disk = Layouts.UsbStick();
        var snapshot = Layouts.Snapshot(disk);

        Assert.Throws<ArgumentException>(() => Evaluate(snapshot, ActionKind.FormatVolume, disk));
        Assert.Throws<ArgumentException>(() => Evaluate(snapshot, ActionKind.ClearDisk, disk.Partitions[0]));
    }
}
