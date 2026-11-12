using WinParted.Core.Actions;
using WinParted.Core.Devices;
using WinParted.Core.Model;
using WinParted.Core.Operations;
using WinParted.Storage;

namespace WinParted.Storage.IntegrationTests;

/// <summary>Groups 7.1 and 9: every operation against a throwaway VHDX. Needs elevation.</summary>
public sealed class OperationTests : IDisposable
{
    private const ulong MB = 1024UL * 1024;

    private readonly SmpStorageProvider _provider = new();
    private readonly TestVhd _vhd;

    public OperationTests()
    {
        if (TestVhd.IsElevated)
        {
            _vhd = TestVhd.Create("ops", 1024, _provider);
        }
        else
        {
            _vhd = null!;
        }
    }

    public void Dispose()
    {
        _vhd?.Dispose();
        _provider.Dispose();
    }

    // ----- helpers --------------------------------------------------------------------------

    private Task Initialize(PartitionStyle style) => _vhd.Execute(s =>
        new InitializeDiskRequest(new DiskTarget(_vhd.Id), TestVhd.Fingerprint(s, new DiskTarget(_vhd.Id)), style));

    private Task Create(ulong size, FormatOptions? format = null, char? letter = null) => _vhd.Execute(s =>
    {
        var region = s.FindDisk(_vhd.Id)!.UnallocatedSegments.First();
        var target = new UnallocatedTarget(_vhd.Id, region.Offset);
        return new CreatePartitionRequest(target, TestVhd.Fingerprint(s, target), size, format, letter);
    });

    private static PartitionTarget Target(Partition p) => new(p.Id);

    private async Task<Partition> DataPartition() =>
        (await _vhd.DiskAsync()).Partitions.Last(p => p.Content != PartitionContent.Reserved);

    // ----- 7.1 reading ------------------------------------------------------------------------

    [AdminFact]
    public async Task Snapshot_shows_two_partitions_and_free_space()
    {
        await Initialize(PartitionStyle.Gpt);
        await Create(200 * MB, new FormatOptions(FileSystemKind.Ntfs, "ONE"));
        await Create(300 * MB, new FormatOptions(FileSystemKind.ExFat, "TWO"));

        var disk = await _vhd.DiskAsync();
        Assert.Equal(PartitionStyle.Gpt, disk.Style);
        Assert.Equal(BusType.Virtual, disk.Bus);

        var data = disk.Partitions.Where(p => p.Content == PartitionContent.Volume).ToList();
        Assert.Equal(["ONE", "TWO"], data.Select(p => p.Volume!.Label));
        Assert.Equal([FileSystemKind.Ntfs, FileSystemKind.ExFat], data.Select(p => p.Volume!.FileSystem));
        Assert.All(data, p => Assert.Null(p.DriveLetter));
        Assert.InRange(data[0].Size, 199 * MB, 201 * MB);

        // GPT Initialize adds a 16 MB Microsoft Reserved partition (spike 2.3).
        Assert.Contains(disk.Partitions, p => p.GptType == KnownPartitionTypes.GptMicrosoftReserved);
        Assert.IsType<UnallocatedSegment>(disk.Layout.Last());
    }

    // ----- 9.1 disk operations -----------------------------------------------------------------

    [AdminFact]
    public async Task Initialize_offline_online_and_clear()
    {
        Assert.Equal(PartitionStyle.Uninitialized, (await _vhd.DiskAsync()).Style);

        await Initialize(PartitionStyle.Mbr);
        Assert.Equal(PartitionStyle.Mbr, (await _vhd.DiskAsync()).Style);

        await Create(100 * MB, new FormatOptions(FileSystemKind.Ntfs, null));

        await _vhd.Execute(s => new SetDiskOnlineRequest(new DiskTarget(_vhd.Id), TestVhd.Fingerprint(s, new DiskTarget(_vhd.Id)), Online: false));
        Assert.True((await _vhd.DiskAsync()).IsOffline);

        await _vhd.Execute(s => new SetDiskOnlineRequest(new DiskTarget(_vhd.Id), TestVhd.Fingerprint(s, new DiskTarget(_vhd.Id)), Online: true));
        Assert.False((await _vhd.DiskAsync()).IsOffline);

        await _vhd.Execute(s => new ClearDiskRequest(new DiskTarget(_vhd.Id), TestVhd.Fingerprint(s, new DiskTarget(_vhd.Id))));
        var cleared = await _vhd.DiskAsync();
        Assert.Empty(cleared.Partitions);
        Assert.Equal(PartitionStyle.Uninitialized, cleared.Style);
    }

    [AdminFact]
    public async Task Initializing_twice_is_reported_plainly()
    {
        await Initialize(PartitionStyle.Gpt);

        var ex = await Assert.ThrowsAsync<StorageOperationException>(() => Initialize(PartitionStyle.Gpt));
        Assert.Equal(41001u, ex.Error.Code);
        Assert.False(ex.Error.MayHaveChanged);
    }

    // ----- 9.2 create and delete ---------------------------------------------------------------

    [AdminFact]
    public async Task Create_without_format_has_no_letter_and_delete_frees_space()
    {
        await Initialize(PartitionStyle.Gpt);
        await Create(100 * MB);

        var raw = await DataPartition();
        Assert.Equal(PartitionContent.Unformatted, raw.Content);
        Assert.Null(raw.DriveLetter);

        await _vhd.Execute(s => new DeletePartitionRequest(Target(raw), TestVhd.Fingerprint(s, Target(raw))));
        var disk = await _vhd.DiskAsync();
        Assert.DoesNotContain(disk.Partitions, p => p.Id == raw.Id);
        Assert.Single(disk.UnallocatedSegments);
    }

    [AdminFact]
    public async Task Create_then_failed_format_leaves_an_unformatted_partition()
    {
        // Full NTFS is refused on virtual disks (spike 2.4), which makes the second step fail.
        await Initialize(PartitionStyle.Gpt);

        var ex = await Assert.ThrowsAsync<StorageOperationException>(() =>
            Create(100 * MB, new FormatOptions(FileSystemKind.Ntfs, null, Quick: false)));

        Assert.True(ex.Error.MayHaveChanged);
        Assert.Contains("partition was created", ex.Error.Message, StringComparison.Ordinal);
        var partition = await DataPartition();
        Assert.Equal(PartitionContent.Unformatted, partition.Content);
        Assert.Null(partition.DriveLetter);
    }

    // ----- 9.3 format ----------------------------------------------------------------------------

    [AdminFact]
    public async Task Quick_format_each_file_system()
    {
        await Initialize(PartitionStyle.Gpt);
        await Create(300 * MB, new FormatOptions(FileSystemKind.Ntfs, null));

        foreach (var fs in new[] { FileSystemKind.ExFat, FileSystemKind.Fat32, FileSystemKind.Ntfs })
        {
            var p = await DataPartition();
            await _vhd.Execute(s => new FormatVolumeRequest(Target(p), TestVhd.Fingerprint(s, Target(p)), new FormatOptions(fs, "FMT")));
            var after = await DataPartition();
            Assert.Equal(fs, after.Volume!.FileSystem);
            Assert.Equal("FMT", after.Volume.Label);
        }
    }

    [AdminFact]
    public async Task Full_format_reports_progress_and_full_ntfs_on_vhdx_is_refused()
    {
        await Initialize(PartitionStyle.Gpt);
        await Create(300 * MB, new FormatOptions(FileSystemKind.Ntfs, null));
        var reports = new List<OperationProgress>();
        var progress = new SynchronousProgress(reports.Add);

        var p = await DataPartition();
        await _vhd.Execute(s => new FormatVolumeRequest(Target(p), TestVhd.Fingerprint(s, Target(p)), new FormatOptions(FileSystemKind.ExFat, null, Quick: false)), progress);
        Assert.Equal(FileSystemKind.ExFat, (await DataPartition()).Volume!.FileSystem);
        Assert.NotEmpty(reports);

        p = await DataPartition();
        var ex = await Assert.ThrowsAsync<StorageOperationException>(() => _vhd.Execute(s =>
            new FormatVolumeRequest(Target(p), TestVhd.Fingerprint(s, Target(p)), new FormatOptions(FileSystemKind.Ntfs, null, Quick: false))));
        Assert.Equal(1u, ex.Error.Code);
        Assert.Contains("full format", ex.Error.Message, StringComparison.Ordinal);
    }

    [AdminFact]
    public async Task Cluster_sizes_are_offered()
    {
        await Initialize(PartitionStyle.Gpt);
        await Create(300 * MB, new FormatOptions(FileSystemKind.Ntfs, null));

        var sizes = await _provider.GetSupportedClusterSizesAsync((await DataPartition()).Id, FileSystemKind.Ntfs);
        Assert.Contains(4096u, sizes);
    }

    // ----- 9.4 shrink and extend ---------------------------------------------------------------

    [AdminFact]
    public async Task Shrink_and_extend_ntfs()
    {
        await Initialize(PartitionStyle.Gpt);
        await Create(600 * MB, new FormatOptions(FileSystemKind.Ntfs, null));
        var p = await DataPartition();

        var range = await _provider.GetSupportedSizeAsync(p.Id);
        Assert.True(range.Minimum < p.Size && range.Maximum >= p.Size);

        var smaller = 300 * MB;
        await _vhd.Execute(s => new ResizePartitionRequest(Target(p), TestVhd.Fingerprint(s, Target(p)), smaller, IsShrink: true));
        var shrunk = await DataPartition();
        Assert.Equal(smaller, shrunk.Size);
        Assert.IsType<UnallocatedSegment>((await _vhd.DiskAsync()).Layout.Last());

        var max = (await _provider.GetSupportedSizeAsync(shrunk.Id)).Maximum;
        await _vhd.Execute(s => new ResizePartitionRequest(Target(shrunk), TestVhd.Fingerprint(s, Target(shrunk)), max, IsShrink: false));
        Assert.Equal(max, (await DataPartition()).Size);
    }

    [AdminFact]
    public async Task Resize_below_minimum_is_reported_plainly()
    {
        await Initialize(PartitionStyle.Gpt);
        await Create(300 * MB, new FormatOptions(FileSystemKind.Ntfs, null));
        var p = await DataPartition();
        var min = (await _provider.GetSupportedSizeAsync(p.Id)).Minimum;

        var ex = await Assert.ThrowsAsync<StorageOperationException>(() => _vhd.Execute(s =>
            new ResizePartitionRequest(Target(p), TestVhd.Fingerprint(s, Target(p)), min / 2, IsShrink: true)));
        Assert.Equal(4097u, ex.Error.Code);
        Assert.False(ex.Error.MayHaveChanged);
    }

    // ----- 9.5 drive letter and label ----------------------------------------------------------

    [AdminFact]
    public async Task Drive_letter_add_change_remove_and_label()
    {
        await Initialize(PartitionStyle.Gpt);
        await Create(100 * MB, new FormatOptions(FileSystemKind.Ntfs, "OLD"));
        var free = (await _vhd.SnapshotAsync()).Facts.FreeDriveLetters.Reverse().Take(2).ToArray();
        Assert.True(free.Length == 2, "Needs two free drive letters.");

        var p = await DataPartition();
        await _vhd.Execute(s => new SetDriveLetterRequest(Target(p), TestVhd.Fingerprint(s, Target(p)), free[0]));
        Assert.Equal(free[0], (await DataPartition()).DriveLetter);

        p = await DataPartition();
        await _vhd.Execute(s => new SetDriveLetterRequest(Target(p), TestVhd.Fingerprint(s, Target(p)), free[1]));
        Assert.Equal(free[1], (await DataPartition()).DriveLetter);

        p = await DataPartition();
        await _vhd.Execute(s => new SetLabelRequest(Target(p), TestVhd.Fingerprint(s, Target(p)), "Projects"));
        Assert.Equal("Projects", (await DataPartition()).Volume!.Label);

        p = await DataPartition();
        await _vhd.Execute(s => new SetDriveLetterRequest(Target(p), TestVhd.Fingerprint(s, Target(p)), null));
        Assert.Null((await DataPartition()).DriveLetter);
    }

    /// <summary>Progress&lt;T&gt; posts asynchronously; tests need the reports collected inline.</summary>
    private sealed class SynchronousProgress(Action<OperationProgress> report) : IProgress<OperationProgress>
    {
        public void Report(OperationProgress value)
        {
            lock (this)
            {
                report(value);
            }
        }
    }
}

/// <summary>Task 10.1 (integration part): attaching a disk triggers one refresh within 3 seconds.</summary>
public class DeviceChangeTests
{
    [AdminFact]
    public async Task Attaching_a_vhdx_triggers_a_refresh()
    {
        using var source = new SmpDeviceChangeSource();
        using var debouncer = new ChangeDebouncer();
        debouncer.Attach(source);
        var settled = new TaskCompletionSource<DateTime>();
        debouncer.Settled += (_, _) => settled.TrySetResult(DateTime.UtcNow);
        source.Start();
        Assert.Empty(source.StartErrors);

        using var provider = new SmpStorageProvider();
        using var vhd = TestVhd.Create("events", 256, provider);

        var winner = await Task.WhenAny(settled.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(settled.Task, winner);
        Assert.True(settled.Task.Result <= vhd.AttachedAt.AddSeconds(3), "Refresh came more than 3 s after the disk was attached.");
    }
}
