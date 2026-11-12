using WinParted.Core.Actions;
using WinParted.Core.Model;

namespace WinParted.Core.Tests.Actions;

internal static class AvailabilityAssert
{
    public static ActionState Evaluate(StorageSnapshot snapshot, ActionKind kind, Partition partition, bool isBusy = false) =>
        ActionAvailability.Evaluate(snapshot, kind, new PartitionTarget(partition.Id), isBusy);

    public static ActionState Evaluate(StorageSnapshot snapshot, ActionKind kind, Disk disk, bool isBusy = false) =>
        ActionAvailability.Evaluate(snapshot, kind, new DiskTarget(disk.Id), isBusy);

    public static void Enabled(ActionState state) =>
        Assert.True(state.IsEnabled, $"{state.Kind} should be enabled but was disabled: {state.DisabledReason?.Code}");

    public static void Disabled(ActionState state, ReasonCode expected) =>
        Assert.Equal(expected, state.DisabledReason?.Code);
}
