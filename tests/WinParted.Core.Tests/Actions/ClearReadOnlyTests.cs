using WinParted.Core.Actions;
using WinParted.Core.Model;
using WinParted.Core.Tests.Builders;
using static WinParted.Core.Tests.Actions.AvailabilityAssert;
using static WinParted.Core.Tests.Builders.DiskBuilder;

namespace WinParted.Core.Tests.Actions;

/// <summary>Spec disk-operations "Turn off read-only" (task 2.1).</summary>
public class ClearReadOnlyTests
{
    private static ActionKind[] DiskActions(Disk disk) =>
        [.. ActionAvailability.RelevantActions(Layouts.Snapshot(disk), new DiskTarget(disk.Id))];

    [Fact]
    public void Offered_first_on_a_read_only_disk()
    {
        var disk = new DiskBuilder(4) { IsReadOnly = true }.Vol(64 * GB, letter: 'V').Build();

        Assert.Equal(ActionKind.ClearReadOnly, DiskActions(disk)[0]);
        Enabled(Evaluate(Layouts.Snapshot(disk), ActionKind.ClearReadOnly, disk));
    }

    [Fact]
    public void Not_offered_on_a_writable_disk()
    {
        var disk = Layouts.UsbStick(2);

        Assert.DoesNotContain(ActionKind.ClearReadOnly, DiskActions(disk));
        Disabled(Evaluate(Layouts.Snapshot(disk), ActionKind.ClearReadOnly, disk), ReasonCode.DiskNotReadOnly);
    }

    [Fact]
    public void Offered_on_an_offline_read_only_disk_next_to_bring_online()
    {
        var disk = new DiskBuilder(4) { IsOffline = true, IsReadOnly = true }.Vol(64 * GB, letter: 'V').Build();

        Assert.Equal([ActionKind.ClearReadOnly, ActionKind.Initialize, ActionKind.BringOnline, ActionKind.ClearDisk], DiskActions(disk));
        Enabled(Evaluate(Layouts.Snapshot(disk), ActionKind.ClearReadOnly, disk));
        Enabled(Evaluate(Layouts.Snapshot(disk), ActionKind.BringOnline, disk));
    }

    [Fact]
    public void Managed_disks_keep_their_reason()
    {
        var spaces = new DiskBuilder(4) { IsReadOnly = true, IsStorageSpacesMember = true }.Build();
        var dynamic = new DiskBuilder(5) { IsReadOnly = true }
            .Part(1 * MB, p => p with { GptType = KnownPartitionTypes.GptLdmMetadata })
            .Part(500 * GB, p => p with { GptType = KnownPartitionTypes.GptLdmData })
            .Build();

        Disabled(Evaluate(Layouts.Snapshot(spaces), ActionKind.ClearReadOnly, spaces), ReasonCode.StorageSpaces);
        Disabled(Evaluate(Layouts.Snapshot(dynamic), ActionKind.ClearReadOnly, dynamic), ReasonCode.DynamicDisk);
    }

    [Fact]
    public void Busy_wins()
    {
        var disk = new DiskBuilder(4) { IsReadOnly = true }.Build();
        Disabled(Evaluate(Layouts.Snapshot(disk), ActionKind.ClearReadOnly, disk, isBusy: true), ReasonCode.OperationInProgress);
    }
}
