using WinParted.Core.Model;
using WinParted.Core.Protection;
using WinParted.Core.Tests.Builders;
using static WinParted.Core.Tests.Builders.DiskBuilder;

namespace WinParted.Core.Tests.Protection;

/// <summary>One test per row of the design D6 detection table (task 5.1).</summary>
public class ProtectionClassifierTests
{
    private static Partition Single(Func<Partition, Partition> configure, PartitionStyle style = PartitionStyle.Gpt) =>
        new DiskBuilder(1, style).Part(GB, configure).Build().Partitions[0];

    [Fact]
    public void Gpt_efi_partition_is_fully_locked()
    {
        var p = ProtectionClassifier.Classify(Single(p => p with { GptType = KnownPartitionTypes.GptEfiSystem }));
        Assert.True(p.IsFullyLocked);
        Assert.Equal(ProtectionRole.EfiSystem, p.Role);
    }

    [Fact]
    public void Mbr_efi_partition_is_fully_locked()
    {
        var p = ProtectionClassifier.Classify(Single(p => p with { MbrType = 0xEF }, PartitionStyle.Mbr));
        Assert.Equal(ProtectionRole.EfiSystem, p.Role);
    }

    [Fact]
    public void Msr_is_fully_locked()
    {
        var p = ProtectionClassifier.Classify(Single(p => p with { GptType = KnownPartitionTypes.GptMicrosoftReserved }));
        Assert.True(p.IsFullyLocked);
        Assert.Equal(ProtectionRole.MicrosoftReserved, p.Role);
    }

    [Theory]
    [InlineData(PartitionStyle.Gpt)]
    [InlineData(PartitionStyle.Mbr)]
    public void Recovery_is_fully_locked(PartitionStyle style)
    {
        var p = ProtectionClassifier.Classify(Single(
            p => style == PartitionStyle.Gpt ? p with { GptType = KnownPartitionTypes.GptRecovery } : p with { MbrType = 0x27 },
            style));
        Assert.True(p.IsFullyLocked);
        Assert.Equal(ProtectionRole.Recovery, p.Role);
    }

    [Fact]
    public void Smp_system_flag_marks_system_reserved_partition()
    {
        var p = ProtectionClassifier.Classify(Layouts.LegacyMbr().Partitions[0]);
        Assert.True(p.IsFullyLocked);
        Assert.Equal(ProtectionRole.SystemPartition, p.Role);
    }

    [Fact]
    public void Boot_flag_marks_windows_volume_as_restricted_not_locked()
    {
        var p = ProtectionClassifier.Classify(Single(p => p with { IsBoot = true }));
        Assert.True(p.IsRestricted);
        Assert.False(p.IsFullyLocked);
        Assert.Equal(ProtectionRole.WindowsVolume, p.Role);
    }

    [Fact]
    public void Volume_containing_SystemRoot_is_windows_volume_even_without_boot_flag()
    {
        var disk = new DiskBuilder(1).Vol(GB, letter: 'C', volume: v => v with { IsWindowsVolume = true }).Build();
        Assert.True(ProtectionClassifier.Classify(disk.Partitions[0]).Flags.HasFlag(PartitionProtectionFlags.WindowsVolume));
    }

    [Fact]
    public void Pagefile_volume_is_restricted()
    {
        var disk = new DiskBuilder(1).Vol(GB, letter: 'D', volume: v => v with { HasPagefileOrDump = true }).Build();
        var p = ProtectionClassifier.Classify(disk.Partitions[0]);
        Assert.True(p.IsRestricted);
        Assert.Equal(ProtectionRole.PagefileVolume, p.Role);
    }

    [Fact]
    public void Unknown_pagefile_state_fails_closed()
    {
        var disk = new DiskBuilder(1).Vol(GB, letter: 'D', volume: v => v with { HasPagefileOrDump = null }).Build();
        var p = ProtectionClassifier.Classify(disk.Partitions[0]);
        Assert.True(p.IsFullyLocked);
        Assert.Equal(ProtectionRole.ProtectionUnknown, p.Role);
    }

    [Fact]
    public void Locked_bitlocker_volume_is_fully_locked()
    {
        var p = ProtectionClassifier.Classify(Layouts.BitLocker(BitLockerState.Locked).Partitions[0]);
        Assert.True(p.IsFullyLocked);
        Assert.Equal(ProtectionRole.BitLockerLocked, p.Role);
    }

    [Fact]
    public void Unlocked_bitlocker_volume_is_not_protected()
    {
        Assert.False(ProtectionClassifier.Classify(Layouts.BitLocker(BitLockerState.Unlocked).Partitions[0]).IsProtected);
    }

    [Fact]
    public void Unknown_bitlocker_state_fails_closed()
    {
        Assert.True(ProtectionClassifier.Classify(Layouts.BitLocker(BitLockerState.Unknown).Partitions[0]).IsFullyLocked);
    }

    [Fact]
    public void Plain_data_volume_is_not_protected()
    {
        Assert.False(ProtectionClassifier.Classify(Layouts.UsbStick().Partitions[0]).IsProtected);
    }

    [Fact]
    public void Disk_holding_windows_is_system_disk()
    {
        var d = ProtectionClassifier.Classify(Layouts.Windows11Default());
        Assert.True(d.IsSystemDisk);
        Assert.False(d.IsWholeDiskLocked);
        Assert.Equal(ProtectionRole.SystemDisk, d.Role);
    }

    [Fact]
    public void Disk_with_windows_partition_is_system_disk_even_without_disk_flags()
    {
        var disk = new DiskBuilder(1).Vol(GB, partition: p => p with { IsBoot = true }).Build();
        Assert.True(ProtectionClassifier.Classify(disk).IsSystemDisk);
    }

    [Fact]
    public void Dynamic_disk_is_whole_disk_locked()
    {
        var d = ProtectionClassifier.Classify(Layouts.DynamicDisk());
        Assert.True(d.IsWholeDiskLocked);
        Assert.Equal(ProtectionRole.DynamicDisk, d.Role);
    }

    [Fact]
    public void Mbr_ldm_partition_marks_dynamic_disk()
    {
        var disk = new DiskBuilder(1, PartitionStyle.Mbr).Part(GB, p => p with { MbrType = 0x42 }).Build();
        Assert.True(ProtectionClassifier.Classify(disk).Flags.HasFlag(DiskProtectionFlags.DynamicDisk));
    }

    [Fact]
    public void Storage_spaces_member_is_whole_disk_locked()
    {
        var d = ProtectionClassifier.Classify(new DiskBuilder(1) { IsStorageSpacesMember = true }.Build());
        Assert.True(d.IsWholeDiskLocked);
        Assert.Equal(ProtectionRole.StorageSpaces, d.Role);
    }

    [Fact]
    public void Storage_spaces_partition_type_marks_disk()
    {
        var disk = new DiskBuilder(1).Part(GB, p => p with { GptType = KnownPartitionTypes.GptStorageSpaces }).Build();
        Assert.True(ProtectionClassifier.Classify(disk).Flags.HasFlag(DiskProtectionFlags.StorageSpaces));
    }

    [Fact]
    public void Unknown_storage_spaces_membership_fails_closed()
    {
        var d = ProtectionClassifier.Classify(new DiskBuilder(1) { IsStorageSpacesMember = null }.Build());
        Assert.True(d.IsWholeDiskLocked);
        Assert.Equal(ProtectionRole.ProtectionUnknown, d.Role);
    }

    [Fact]
    public void Read_only_disk_is_whole_disk_locked()
    {
        var d = ProtectionClassifier.Classify(new DiskBuilder(1) { IsReadOnly = true }.Build());
        Assert.True(d.IsWholeDiskLocked);
        Assert.Equal(ProtectionRole.ReadOnlyDisk, d.Role);
    }

    [Fact]
    public void Usb_stick_is_not_protected()
    {
        Assert.False(ProtectionClassifier.Classify(Layouts.UsbStick()).IsProtected);
    }
}
