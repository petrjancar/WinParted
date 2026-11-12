using System.Globalization;
using WinParted.Core.Formatting;
using WinParted.Core.Model;
using WinParted.Core.Tests.Builders;
using static WinParted.Core.Tests.Builders.DiskBuilder;

namespace WinParted.Core.Tests.Layout;

public class LayoutBuilderTests
{
    [Fact]
    public void Windows11_layout_has_four_partitions_then_free_space()
    {
        var layout = Layouts.Windows11Default().Layout;

        Assert.Equal(5, layout.Length);
        Assert.All(layout.Take(4), s => Assert.IsType<PartitionSegment>(s));
        var free = Assert.IsType<UnallocatedSegment>(layout[4]);

        // The free region runs to the last usable byte, just before the GPT backup header.
        Assert.InRange(free.Size, 100 * GB, (100 * GB) + MB);
        Assert.Equal(Layouts.Windows11Default().Size - (33 * 512), free.End);
    }

    [Fact]
    public void Free_space_at_start()
    {
        var disk = new DiskBuilder(1).Free(10 * GB).Vol(20 * GB).Build();

        Assert.Collection(
            disk.Layout,
            s => Assert.Equal((MB, 10 * GB), (Assert.IsType<UnallocatedSegment>(s).Offset, s.Size)),
            s => Assert.IsType<PartitionSegment>(s));
    }

    [Fact]
    public void Free_space_in_middle()
    {
        var disk = new DiskBuilder(1).Vol(20 * GB).Free(5 * GB).Vol(20 * GB).Build();

        Assert.Collection(
            disk.Layout,
            s => Assert.IsType<PartitionSegment>(s),
            s => Assert.Equal(5 * GB, Assert.IsType<UnallocatedSegment>(s).Size),
            s => Assert.IsType<PartitionSegment>(s));
    }

    [Fact]
    public void No_free_space()
    {
        var disk = new DiskBuilder(1).Vol(20 * GB).Vol(20 * GB).Build();

        Assert.All(disk.Layout, s => Assert.IsType<PartitionSegment>(s));
        Assert.Equal(0UL, disk.UnallocatedSize);
    }

    [Fact]
    public void Gaps_of_one_megabyte_or_less_are_ignored()
    {
        // The builder starts at 1 MB and Build() adds a 1 MB tail: both gaps are alignment, not free space.
        var disk = new DiskBuilder(1).Vol(20 * GB).Free(MB).Vol(20 * GB).Build();

        Assert.Equal(2, disk.Layout.Length);
    }

    [Fact]
    public void Gap_just_over_one_megabyte_is_shown()
    {
        var disk = new DiskBuilder(1).Vol(20 * GB).Free(MB + 4096).Vol(20 * GB).Build();

        Assert.Equal(3, disk.Layout.Length);
    }

    [Fact]
    public void Uninitialized_and_offline_disks_have_no_layout()
    {
        Assert.Empty(Layouts.Uninitialized().Layout);
        Assert.Empty(new DiskBuilder(1) { IsOffline = true }.Vol(10 * GB).Build().Layout);
    }

    [Fact]
    public void Mbr_extended_container_is_not_a_segment_but_its_logical_partitions_are()
    {
        var b = new DiskBuilder(1, PartitionStyle.Mbr);
        var diskId = b.Id;
        var disk = new Disk
        {
            Id = diskId,
            Style = PartitionStyle.Mbr,
            Size = 100 * GB,
            Partitions =
            [
                new Partition { Id = new(diskId, MB), Size = 50 * GB, MbrType = 0x07 },
                new Partition { Id = new(diskId, MB + (50 * GB)), Size = 40 * GB, MbrType = 0x0F },
                new Partition { Id = new(diskId, (2 * MB) + (50 * GB)), Size = 20 * GB, MbrType = 0x07 },
            ],
        };

        var partitions = disk.Layout.OfType<PartitionSegment>().ToList();
        Assert.Equal(2, partitions.Count);
        Assert.DoesNotContain(partitions, s => s.Partition.MbrType == 0x0F);
    }
}

public class SizeFormatterTests
{
    [Theory]
    [InlineData(0UL, "0 bytes")]
    [InlineData(512UL, "512 bytes")]
    [InlineData(16UL * MB, "16.0 MB")]
    [InlineData(100UL * MB, "100 MB")]
    [InlineData(750UL * MB, "750 MB")]
    [InlineData(1000UL * 1024, "0.97 MB")]
    [InlineData(476UL * GB, "476 GB")]
    [InlineData(1200UL * GB, "1.17 TB")]
    public void Formats_like_explorer(ulong bytes, string expected)
    {
        Assert.Equal(expected, SizeFormatter.Format(bytes, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Truncates_instead_of_rounding()
    {
        Assert.Equal("7.42 GB", SizeFormatter.Format((ulong)(7.429 * GB), CultureInfo.InvariantCulture));
        Assert.Equal("57.3 GB", SizeFormatter.Format((ulong)(57.39 * GB), CultureInfo.InvariantCulture));
        Assert.Equal("1.82 TB", SizeFormatter.Format((ulong)(1.829 * TB), CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Uses_culture_decimal_separator()
    {
        Assert.Equal("57,3 GB", SizeFormatter.Format((ulong)(57.39 * GB), new CultureInfo("cs-CZ")));
    }
}
