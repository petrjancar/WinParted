using WinParted.Core.Actions;
using WinParted.Core.Model;
using WinParted.Core.Operations;
using WinParted.Core.Tests.Builders;
using static WinParted.Core.Tests.Builders.DiskBuilder;

namespace WinParted.Core.Tests.Operations;

internal sealed class FakeStorageProvider : IStorageProvider
{
    public required StorageSnapshot Current { get; set; }

    public List<OperationRequest> Executed { get; } = [];

    public int Reads { get; private set; }

    public Func<OperationRequest, Task>? OnExecute { get; set; }

    public Task<StorageSnapshot> ReadSnapshotAsync(CancellationToken cancellationToken = default)
    {
        Reads++;
        return Task.FromResult(Current);
    }

    public async Task ExecuteAsync(OperationRequest request, IProgress<OperationProgress>? progress)
    {
        Executed.Add(request);
        if (OnExecute is not null)
        {
            await OnExecute(request);
        }
    }

    public Task<SupportedSizeRange> GetSupportedSizeAsync(PartitionId partition, CancellationToken cancellationToken = default) =>
        Task.FromResult(new SupportedSizeRange(0, 0));

    public Task<IReadOnlyList<uint>> GetSupportedClusterSizesAsync(PartitionId partition, FileSystemKind fileSystem, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<uint>>([4096]);
}

/// <summary>Task 8.1: single slot, re-verification, busy state and post-operation refresh.</summary>
public class OperationRunnerTests
{
    private static (FakeStorageProvider Provider, OperationRunner Runner, Disk Usb) Setup()
    {
        var usb = Layouts.UsbStick(3);
        var provider = new FakeStorageProvider { Current = Layouts.Snapshot(Layouts.Windows11Default(), usb) };
        return (provider, new OperationRunner(provider), usb);
    }

    private static ClearDiskRequest Clear(StorageSnapshot snapshot, Disk disk)
    {
        var target = new DiskTarget(disk.Id);
        return new ClearDiskRequest(target, TargetFingerprint.Of(snapshot, target)!);
    }

    [Fact]
    public async Task Confirmed_operation_runs_and_refreshes()
    {
        var (provider, runner, usb) = Setup();
        var request = Clear(provider.Current, usb);
        var cleared = Layouts.Snapshot(Layouts.Windows11Default());
        provider.OnExecute = _ =>
        {
            provider.Current = cleared;
            return Task.CompletedTask;
        };

        var result = await runner.RunAsync(request);

        Assert.Equal(OperationOutcome.Succeeded, result.Outcome);
        Assert.Same(request, Assert.Single(provider.Executed));
        Assert.Same(cleared, result.Snapshot);
        Assert.Equal(2, provider.Reads);
        Assert.False(runner.IsBusy);
    }

    [Fact]
    public async Task Disk_number_reassigned()
    {
        // spec operation-execution "Disk number reassigned": Disk 3 was removed and another disk is now 3.
        var (provider, runner, usb) = Setup();
        var request = Clear(provider.Current, usb);
        provider.Current = Layouts.Snapshot(Layouts.Windows11Default(), Layouts.UsbStick(3));

        var result = await runner.RunAsync(request);

        Assert.Equal(OperationOutcome.TargetChanged, result.Outcome);
        Assert.Equal(ReasonCode.TargetChanged, result.Reason?.Code);
        Assert.True(result.NothingModified);
        Assert.Empty(provider.Executed);
    }

    [Fact]
    public async Task Same_disk_with_new_number_is_also_aborted()
    {
        // The user typed "3"; if the same disk is now Disk 4, what they confirmed no longer matches.
        var (provider, runner, usb) = Setup();
        var request = Clear(provider.Current, usb);
        var renumbered = new Disk
        {
            Id = usb.Id, Number = 4, Size = usb.Size, Style = usb.Style, Bus = usb.Bus, Partitions = usb.Partitions,
        };
        provider.Current = Layouts.Snapshot(Layouts.Windows11Default(), renumbered);

        Assert.Equal(OperationOutcome.TargetChanged, (await runner.RunAsync(request)).Outcome);
        Assert.Empty(provider.Executed);
    }

    [Fact]
    public async Task Changed_partition_is_aborted()
    {
        var (provider, runner, usb) = Setup();
        var target = new PartitionTarget(usb.Partitions[0].Id);
        var request = new FormatVolumeRequest(target, TargetFingerprint.Of(provider.Current, target)!, new FormatOptions(FileSystemKind.Ntfs, "X"));

        // Someone relabelled the volume in Explorer after the dialog opened.
        var changed = new Disk
        {
            Id = usb.Id, Number = 3, Size = usb.Size, Style = usb.Style,
            Partitions = [usb.Partitions[0] with { Volume = usb.Partitions[0].Volume! with { Label = "RENAMED" } }],
        };
        provider.Current = Layouts.Snapshot(changed);

        Assert.Equal(OperationOutcome.TargetChanged, (await runner.RunAsync(request)).Outcome);
        Assert.Empty(provider.Executed);
    }

    [Fact]
    public async Task Disallowed_action_is_rejected_at_execution_time()
    {
        var (provider, runner, _) = Setup();
        var system = provider.Current.Disks[0];
        var request = Clear(provider.Current, system);

        var result = await runner.RunAsync(request);

        Assert.Equal(OperationOutcome.Rejected, result.Outcome);
        Assert.Equal(ReasonCode.SystemDisk, result.Reason?.Code);
        Assert.Empty(provider.Executed);
    }

    [Fact]
    public async Task Second_operation_is_rejected_while_one_runs()
    {
        // spec operation-execution "Second operation blocked"
        var (provider, runner, usb) = Setup();
        var gate = new TaskCompletionSource();
        provider.OnExecute = _ => gate.Task;
        var busyChanges = 0;
        runner.BusyChanged += (_, _) => busyChanges++;

        var first = runner.RunAsync(Clear(provider.Current, usb));
        Assert.True(runner.IsBusy);
        Assert.NotNull(runner.Current);

        var second = await runner.RunAsync(Clear(provider.Current, usb));
        Assert.Equal(OperationOutcome.Rejected, second.Outcome);
        Assert.Equal(ReasonCode.OperationInProgress, second.Reason?.Code);

        gate.SetResult();
        Assert.Equal(OperationOutcome.Succeeded, (await first).Outcome);
        Assert.False(runner.IsBusy);
        Assert.Null(runner.Current);
        Assert.Equal(2, busyChanges);
        Assert.Single(provider.Executed);
    }

    [Fact]
    public async Task Backend_failure_is_reported_and_view_still_refreshes()
    {
        var (provider, runner, usb) = Setup();
        var error = new StorageError("BACKUP (E:) is in use by another program. Nothing was changed.", MayHaveChanged: false, Code: 42002);
        provider.OnExecute = _ => throw new StorageOperationException(error);

        var result = await runner.RunAsync(Clear(provider.Current, usb));

        Assert.Equal(OperationOutcome.Failed, result.Outcome);
        Assert.Same(error, result.Error);
        Assert.True(result.NothingModified);
        Assert.NotNull(result.Snapshot);
        Assert.Equal(2, provider.Reads);
        Assert.False(runner.IsBusy);
    }

    [Fact]
    public async Task Disk_removed_mid_operation()
    {
        // spec operation-execution "Disk removed mid-operation"
        var (provider, runner, usb) = Setup();
        var target = new PartitionTarget(usb.Partitions[0].Id);
        var format = new FormatVolumeRequest(target, TargetFingerprint.Of(provider.Current, target)!, new FormatOptions(FileSystemKind.Ntfs, null, Quick: false));
        provider.OnExecute = _ =>
        {
            provider.Current = Layouts.Snapshot(Layouts.Windows11Default());
            throw new StorageOperationException(new StorageError("Windows didn't say why.", MayHaveChanged: true, Code: 2));
        };

        var result = await runner.RunAsync(format);

        Assert.Equal(OperationOutcome.Failed, result.Outcome);
        Assert.Equal("The disk was disconnected during the format. The volume may be incomplete. Connect the disk again and format it again.", result.Error!.Message);
        Assert.True(result.Error.MayHaveChanged);
        Assert.Equal(2u, result.Error.Code);
        Assert.Contains("Windows didn't say why.", result.Error.Details, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Failure_on_a_disk_still_present_keeps_the_original_error()
    {
        var (provider, runner, usb) = Setup();
        var error = new StorageError("Windows denied access to this disk. Nothing was changed.", MayHaveChanged: false, Code: 40001);
        provider.OnExecute = _ => throw new StorageOperationException(error);

        var result = await runner.RunAsync(Clear(provider.Current, usb));

        Assert.Same(error, result.Error);
    }

    [Fact]
    public async Task Unexpected_exception_counts_as_possibly_modified()
    {
        var (provider, runner, usb) = Setup();
        provider.OnExecute = _ => throw new InvalidOperationException("boom");

        var result = await runner.RunAsync(Clear(provider.Current, usb));

        Assert.Equal(OperationOutcome.Failed, result.Outcome);
        Assert.True(result.Error!.MayHaveChanged);
        Assert.False(result.NothingModified);
        Assert.Contains("boom", result.Error.Details, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Create_partition_targets_the_unallocated_region()
    {
        var disk = Layouts.DataDiskWithLinux();
        var provider = new FakeStorageProvider { Current = Layouts.Snapshot(disk) };
        var runner = new OperationRunner(provider);
        var free = disk.UnallocatedSegments.Single();
        var target = new UnallocatedTarget(disk.Id, free.Offset);
        var request = new CreatePartitionRequest(target, TargetFingerprint.Of(provider.Current, target)!, 100 * GB,
            new FormatOptions(FileSystemKind.Ntfs, "New"), 'F');

        Assert.Equal(OperationOutcome.Succeeded, (await runner.RunAsync(request)).Outcome);
    }

    [Fact]
    public void Fingerprint_is_null_for_missing_target()
    {
        var snapshot = Layouts.Snapshot(Layouts.Windows11Default());
        Assert.Null(TargetFingerprint.Of(snapshot, new DiskTarget(new DiskId("gone"))));
        Assert.Null(TargetFingerprint.Of(snapshot, new UnallocatedTarget(snapshot.Disks[0].Id, 12345)));
    }
}
