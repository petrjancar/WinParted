using WinParted.Core.Model;
using WinParted.Core.Tests.Builders;
using static WinParted.Core.Tests.Builders.DiskBuilder;

namespace WinParted.Core.Tests.Model;

public class IdentityTests
{
    [Fact]
    public void DiskIds_with_same_value_are_equal()
    {
        Assert.Equal(new DiskId("abc"), new DiskId("abc"));
        Assert.NotEqual(new DiskId("abc"), new DiskId("abd"));
    }

    [Fact]
    public void PartitionId_is_disk_plus_offset()
    {
        var disk = new DiskId("abc");
        Assert.Equal(new PartitionId(disk, MB), new PartitionId(disk, MB));
        Assert.NotEqual(new PartitionId(disk, MB), new PartitionId(disk, 2 * MB));
        Assert.NotEqual(new PartitionId(disk, MB), new PartitionId(new DiskId("other"), MB));
    }

    [Fact]
    public void Disk_number_is_not_part_of_identity()
    {
        var a = new Disk { Id = new DiskId("same"), Number = 2 };
        var b = new Disk { Id = new DiskId("same"), Number = 3 };
        var snapshot = new StorageSnapshot { Disks = [b] };

        Assert.Same(b, snapshot.FindDisk(a.Id));
    }

    [Fact]
    public void Snapshot_finds_partition_by_identity()
    {
        var disk = Layouts.Windows11Default();
        var snapshot = Layouts.Snapshot(disk);
        var c = disk.Partitions.Single(p => p.DriveLetter == 'C');

        Assert.Same(c, snapshot.FindPartition(c.Id));
        Assert.Null(snapshot.FindPartition(new PartitionId(disk.Id, 12345)));
    }

    [Fact]
    public void Snapshot_orders_disks_by_number_and_partitions_by_offset()
    {
        var snapshot = Layouts.Snapshot(Layouts.UsbStick(2), Layouts.Windows11Default(0));
        Assert.Equal([0, 2], snapshot.Disks.Select(d => d.Number));

        var reversed = new Disk
        {
            Id = new DiskId("x"),
            Partitions =
            [
                new Partition { Id = new PartitionId(new DiskId("x"), 5 * MB), Size = MB },
                new Partition { Id = new PartitionId(new DiskId("x"), MB), Size = MB },
            ],
        };
        Assert.Equal([MB, 5 * MB], reversed.Partitions.Select(p => p.Offset));
    }
}

public class PartitionContentTests
{
    [Fact]
    public void Readable_volume_is_volume()
    {
        var c = Layouts.Windows11Default().Partitions.Single(p => p.DriveLetter == 'C');
        Assert.Equal(PartitionContent.Volume, c.Content);
    }

    [Fact]
    public void Windows_reserved_types_are_reserved()
    {
        var disk = Layouts.Windows11Default();
        Assert.All(disk.Partitions.Where(p => p.DriveLetter is null), p => Assert.Equal(PartitionContent.Reserved, p.Content));
    }

    [Fact]
    public void Basic_data_without_filesystem_is_unformatted()
    {
        var disk = new DiskBuilder(1).Part(10 * GB).Vol(10 * GB, fs: FileSystemKind.None).Build();
        Assert.All(disk.Partitions, p => Assert.Equal(PartitionContent.Unformatted, p.Content));
    }

    [Fact]
    public void Linux_partition_is_foreign()
    {
        var linux = Layouts.DataDiskWithLinux().Partitions[1];
        Assert.Equal(PartitionContent.Foreign, linux.Content);
    }

    [Fact]
    public void Basic_data_with_unknown_filesystem_is_foreign()
    {
        var disk = new DiskBuilder(1)
            .Vol(10 * GB, fs: FileSystemKind.Other, volume: v => v with { FileSystemName = "ext4" })
            .Build();
        Assert.Equal(PartitionContent.Foreign, disk.Partitions[0].Content);
    }
}
