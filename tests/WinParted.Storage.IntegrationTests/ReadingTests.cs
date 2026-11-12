using WinParted.Core.Actions;
using WinParted.Core.Model;
using WinParted.Core.Operations;
using WinParted.Core.Protection;
using WinParted.Storage;

namespace WinParted.Storage.IntegrationTests;

/// <summary>Read-only checks against this PC. They change nothing and also run unelevated.</summary>
public class SystemDiskTests
{
    [Fact]
    public async Task Running_system_disk_is_classified_as_protected()
    {
        // task 7.2
        using var provider = new SmpStorageProvider();
        var snapshot = await provider.ReadSnapshotAsync();

        var system = Assert.Single(snapshot.Disks, d => d.IsBoot || d.Partitions.Any(p => p.Volume?.IsWindowsVolume == true));
        Assert.True(ProtectionClassifier.Classify(system).IsSystemDisk);

        var windows = Assert.Single(system.Partitions, p => p.Volume?.IsWindowsVolume == true);
        Assert.Equal(char.ToUpperInvariant(Environment.GetEnvironmentVariable("SystemRoot")![0]), windows.DriveLetter);
        Assert.True(ProtectionClassifier.Classify(windows).IsProtected);

        foreach (var kind in new[] { ActionKind.ClearDisk, ActionKind.Initialize, ActionKind.TakeOffline })
        {
            Assert.False(ActionAvailability.Evaluate(snapshot, kind, new DiskTarget(system.Id), false).IsEnabled);
        }

        foreach (var kind in new[] { ActionKind.DeletePartition, ActionKind.FormatVolume })
        {
            Assert.False(ActionAvailability.Evaluate(snapshot, kind, new PartitionTarget(windows.Id), false).IsEnabled);
        }
    }

    [Fact]
    public async Task Efi_and_reserved_partitions_are_locked()
    {
        using var provider = new SmpStorageProvider();
        var snapshot = await provider.ReadSnapshotAsync();
        var system = snapshot.Disks.Single(d => d.IsBoot || d.Partitions.Any(p => p.Volume?.IsWindowsVolume == true));

        foreach (var p in system.Partitions.Where(p => p.Content == PartitionContent.Reserved))
        {
            Assert.True(ProtectionClassifier.Classify(p).IsFullyLocked, $"Partition {p.Number} should be locked.");
        }
    }

    [Fact]
    public async Task Snapshot_layout_covers_the_whole_disk_in_order()
    {
        using var provider = new SmpStorageProvider();
        var snapshot = await provider.ReadSnapshotAsync();

        Assert.NotEmpty(snapshot.Disks);
        foreach (var disk in snapshot.Disks.Where(d => !d.IsOffline && d.Style != PartitionStyle.Uninitialized))
        {
            Assert.Equal(disk.Layout.OrderBy(s => s.Offset), disk.Layout);
            Assert.All(disk.Layout, s => Assert.True(s.End <= disk.Size));
        }
    }
}

/// <summary>Task 7.3: the fixture's guard never lets a request reach another disk.</summary>
public class GuardTests
{
    [AdminFact]
    public async Task Guard_rejects_the_system_disk()
    {
        using var provider = new SmpStorageProvider();
        using var vhd = TestVhd.Create("guard", 256, provider);
        var snapshot = await vhd.SnapshotAsync();
        var system = snapshot.Disks.Single(d => d.IsBoot || d.Partitions.Any(p => p.Volume?.IsWindowsVolume == true));

        await Assert.ThrowsAsync<InvalidOperationException>(() => vhd.Execute(s =>
            new ClearDiskRequest(new DiskTarget(system.Id), TestVhd.Fingerprint(s, new DiskTarget(system.Id)))));
    }
}
