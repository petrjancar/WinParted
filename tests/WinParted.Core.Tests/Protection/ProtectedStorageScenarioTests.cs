using WinParted.Core.Actions;
using WinParted.Core.Model;
using WinParted.Core.Protection;
using WinParted.Core.Tests.Builders;
using WinParted.Core.Text;
using static WinParted.Core.Tests.Actions.AvailabilityAssert;
using static WinParted.Core.Tests.Builders.DiskBuilder;

namespace WinParted.Core.Tests.Protection;

/// <summary>One test per scenario in specs/protected-storage (task 5.2).</summary>
public class ProtectedStorageScenarioTests
{
    private static readonly ActionKind[] AllPartitionActions =
    [
        ActionKind.FormatVolume, ActionKind.DeletePartition, ActionKind.ShrinkVolume,
        ActionKind.ExtendVolume, ActionKind.ChangeDriveLetter, ActionKind.ChangeLabel,
    ];

    [Fact]
    public void EFI_partition_shown()
    {
        var efi = Layouts.Windows11Default().Partitions[0];
        var protection = ProtectionClassifier.Classify(efi);

        Assert.True(protection.IsProtected);
        Assert.Equal("EFI System - Windows needs this to start", Texts.Role(protection.Role!.Value));
    }

    [Fact]
    public void Delete_Recovery_partition()
    {
        var disk = Layouts.Windows11Default();
        var recovery = disk.Partitions[3];
        var snapshot = Layouts.Snapshot(disk);

        foreach (var kind in AllPartitionActions)
        {
            Disabled(Evaluate(snapshot, kind, recovery), ReasonCode.RecoveryPartition);
        }

        Assert.Equal("Windows uses this partition to repair itself.", Reason.Of(ReasonCode.RecoveryPartition).Text);
    }

    [Fact]
    public void Shrink_C()
    {
        var disk = Layouts.Windows11Default();
        var c = disk.Partitions[2];
        var snapshot = Layouts.Snapshot(disk);

        Enabled(Evaluate(snapshot, ActionKind.ShrinkVolume, c));
        Enabled(Evaluate(snapshot, ActionKind.ChangeLabel, c));
        Disabled(Evaluate(snapshot, ActionKind.DeletePartition, c), ReasonCode.WindowsVolume);
        Disabled(Evaluate(snapshot, ActionKind.FormatVolume, c), ReasonCode.WindowsVolume);
        Disabled(Evaluate(snapshot, ActionKind.ChangeDriveLetter, c), ReasonCode.WindowsVolume);
        Assert.Equal("Windows is running from this volume.", Reason.Of(ReasonCode.WindowsVolume).Text);
    }

    [Fact]
    public void Pagefile_on_D()
    {
        var disk = new DiskBuilder(1).Vol(100 * GB, label: "Data", letter: 'D', volume: v => v with { HasPagefileOrDump = true })
            .Free(10 * GB).Build();
        var d = disk.Partitions[0];
        var snapshot = Layouts.Snapshot(disk);

        Disabled(Evaluate(snapshot, ActionKind.DeletePartition, d), ReasonCode.PagefileVolume);
        Disabled(Evaluate(snapshot, ActionKind.FormatVolume, d), ReasonCode.PagefileVolume);
        Enabled(Evaluate(snapshot, ActionKind.ShrinkVolume, d));
        Enabled(Evaluate(snapshot, ActionKind.ExtendVolume, d));
        Assert.Equal("Windows uses this volume for virtual memory.", Reason.Of(ReasonCode.PagefileVolume).Text);
    }

    [Fact]
    public void Locked_encrypted_USB_drive()
    {
        var disk = Layouts.BitLocker(BitLockerState.Locked);
        var snapshot = Layouts.Snapshot(disk);

        foreach (var kind in AllPartitionActions)
        {
            Disabled(Evaluate(snapshot, kind, disk.Partitions[0]), ReasonCode.BitLockerLocked);
        }

        Assert.Equal(
            "This volume is locked by BitLocker. Unlock it in File Explorer first.",
            Reason.Of(ReasonCode.BitLockerLocked).Text);
    }

    [Fact]
    public void Storage_Spaces_member()
    {
        var disk = new DiskBuilder(1) { IsStorageSpacesMember = true }.Vol(100 * GB, letter: 'S').Free(10 * GB).Build();
        var snapshot = Layouts.Snapshot(disk);

        Assert.Equal("Managed by Storage Spaces", Texts.Role(ProtectionClassifier.Classify(disk).Role!.Value));
        foreach (var kind in new[] { ActionKind.Initialize, ActionKind.TakeOffline, ActionKind.ClearDisk })
        {
            Disabled(Evaluate(snapshot, kind, disk), ReasonCode.StorageSpaces);
        }

        foreach (var kind in AllPartitionActions)
        {
            Disabled(Evaluate(snapshot, kind, disk.Partitions[0]), ReasonCode.StorageSpaces);
        }

        var free = disk.UnallocatedSegments.Single();
        Disabled(
            ActionAvailability.Evaluate(snapshot, ActionKind.CreatePartition, new UnallocatedTarget(disk.Id, free.Offset), false),
            ReasonCode.StorageSpaces);
    }

    [Fact]
    public void Clear_system_disk()
    {
        var disk = Layouts.Windows11Default();
        var snapshot = Layouts.Snapshot(disk);

        Disabled(Evaluate(snapshot, ActionKind.ClearDisk, disk), ReasonCode.SystemDisk);
        Disabled(Evaluate(snapshot, ActionKind.Initialize, disk), ReasonCode.SystemDisk);
        Disabled(Evaluate(snapshot, ActionKind.TakeOffline, disk), ReasonCode.SystemDisk);
        Assert.Equal("Windows is installed on this disk.", Reason.Of(ReasonCode.SystemDisk).Text);
    }

    [Fact]
    public void Data_partition_on_system_disk()
    {
        var disk = new DiskBuilder(0) { IsSystem = true, IsBoot = true }
            .Vol(100 * MB, fs: FileSystemKind.Fat32, partition: p => p with { GptType = KnownPartitionTypes.GptEfiSystem, IsSystem = true })
            .Vol(200 * GB, letter: 'C', volume: v => v with { IsWindowsVolume = true }, partition: p => p with { IsBoot = true })
            .Vol(200 * GB, label: "Extra", letter: 'E')
            .Build();
        var e = disk.Partitions[2];
        var snapshot = Layouts.Snapshot(disk);

        Enabled(Evaluate(snapshot, ActionKind.DeletePartition, e));
        Enabled(Evaluate(snapshot, ActionKind.FormatVolume, e));
    }

    [Fact]
    public void Write_protected_SD_card()
    {
        var disk = new DiskBuilder(5) { Bus = BusType.Sd, IsReadOnly = true }.Vol(29 * GB, letter: 'G', fs: FileSystemKind.ExFat).Build();
        var snapshot = Layouts.Snapshot(disk);

        foreach (var kind in AllPartitionActions)
        {
            Disabled(Evaluate(snapshot, kind, disk.Partitions[0]), ReasonCode.DiskReadOnly);
        }

        Disabled(Evaluate(snapshot, ActionKind.ClearDisk, disk), ReasonCode.DiskReadOnly);
        Assert.Contains("write-protected", Reason.Of(ReasonCode.DiskReadOnly).Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Offline_and_read_only_disk()
    {
        var offline = new DiskBuilder(4) { IsOffline = true, IsReadOnly = true }.Vol(64 * GB, letter: 'V').Build();
        var snapshot = Layouts.Snapshot(offline);

        Enabled(Evaluate(snapshot, ActionKind.BringOnline, offline));
        Disabled(Evaluate(snapshot, ActionKind.Initialize, offline), ReasonCode.DiskReadOnly);
        Disabled(Evaluate(snapshot, ActionKind.ClearDisk, offline), ReasonCode.DiskReadOnly);

        // After bringing it online it is still read-only: everything else stays write-protected.
        var online = new DiskBuilder(4) { IsReadOnly = true }.Vol(64 * GB, letter: 'V').Build();
        var after = Layouts.Snapshot(online);
        Disabled(Evaluate(after, ActionKind.TakeOffline, online), ReasonCode.DiskReadOnly);
        foreach (var kind in AllPartitionActions)
        {
            Disabled(Evaluate(after, kind, online.Partitions[0]), ReasonCode.DiskReadOnly);
        }
    }

    [Fact]
    public void Bring_online_stays_locked_on_managed_disks()
    {
        var disk = new DiskBuilder(4) { IsOffline = true, IsStorageSpacesMember = true }.Build();
        Disabled(Evaluate(Layouts.Snapshot(disk), ActionKind.BringOnline, disk), ReasonCode.StorageSpaces);
    }

    [Fact]
    public void No_advanced_unlock()
    {
        // The availability API has no override parameter or mode: the only inputs are the snapshot,
        // the action, the target and whether an operation is running.
        var parameters = typeof(ActionAvailability).GetMethod(nameof(ActionAvailability.Evaluate))!.GetParameters();
        Assert.Equal(["snapshot", "kind", "target", "isBusy"], parameters.Select(p => p.Name));
    }

    [Fact]
    public void Efi_and_msr_are_fully_locked_with_their_reasons()
    {
        var disk = Layouts.Windows11Default();
        var snapshot = Layouts.Snapshot(disk);

        foreach (var kind in AllPartitionActions)
        {
            Disabled(Evaluate(snapshot, kind, disk.Partitions[0]), ReasonCode.SystemPartition);
            Disabled(Evaluate(snapshot, kind, disk.Partitions[1]), ReasonCode.MicrosoftReserved);
        }
    }

    [Fact]
    public void Dynamic_disk_is_read_only()
    {
        var disk = Layouts.DynamicDisk();
        var snapshot = Layouts.Snapshot(disk);

        Disabled(Evaluate(snapshot, ActionKind.ClearDisk, disk), ReasonCode.DynamicDisk);
        Disabled(Evaluate(snapshot, ActionKind.DeletePartition, disk.Partitions[2]), ReasonCode.DynamicDisk);
    }
}
