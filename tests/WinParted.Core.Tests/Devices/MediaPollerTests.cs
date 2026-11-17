using System.Collections.Immutable;
using Microsoft.Extensions.Time.Testing;
using WinParted.Core.Devices;
using WinParted.Core.Model;
using WinParted.Core.Tests.Builders;
using static WinParted.Core.Tests.Builders.DiskBuilder;

namespace WinParted.Core.Tests.Devices;

/// <summary>Change empty-card-reader-slots, D8 (task 3.3): media checks for card reader slots.</summary>
public class MediaPollerTests
{
    private static readonly TimeSpan Tick = MediaPoller.DefaultInterval;

    private static Disk Fixed(int number) => new DiskBuilder(number).Vol(100 * GB, letter: 'C').Build();

    private static Disk EmptySlot(int number) =>
        new DiskBuilder(number, PartitionStyle.Uninitialized) { Bus = BusType.Usb, IsOffline = true, HasNoMedia = true }.Build(totalSize: 0);

    private static Disk Card(int number) =>
        new DiskBuilder(number, PartitionStyle.Mbr) { Bus = BusType.Usb, IsRemovableMedia = true }.Vol(1 * GB, letter: 'D').Build();

    /// <summary>A poller whose reads return <see cref="Windows"/>: what Windows reports right now.</summary>
    private sealed class Harness
    {
        public FakeTimeProvider Time { get; } = new();

        public List<Disk> Windows { get; } = [];

        public List<ImmutableArray<int>> Reads { get; } = [];

        public Func<IReadOnlyList<DiskMediaState>>? Override { get; set; }

        public int Changes { get; private set; }

        public MediaPoller Poller { get; }

        public Harness(params Disk[] disks)
        {
            Windows.AddRange(disks);
            Poller = new MediaPoller(Read, Time);
            Poller.Changed += (_, _) => Changes++;
            Poller.Watch(Layouts.Snapshot(disks));
            Poller.Start();
        }

        private IReadOnlyList<DiskMediaState> Read(ImmutableArray<int> numbers)
        {
            Reads.Add(numbers);
            return Override?.Invoke()
                ?? [.. Windows.Where(d => numbers.Contains(d.Number)).OrderBy(d => d.Number).Select(DiskMediaState.Of)];
        }

        public void Replace(Disk disk)
        {
            Windows.RemoveAll(d => d.Number == disk.Number);
            Windows.Add(disk);
        }
    }

    [Fact]
    public void Watches_empty_slots_and_removable_media_only()
    {
        var snapshot = Layouts.Snapshot(Fixed(0), EmptySlot(1), Card(2));
        Assert.Equal([1, 2], MediaPoller.DisksToWatch(snapshot).ToArray());
    }

    [Fact]
    public void Card_inserted_raises_a_change_once()
    {
        var h = new Harness(Fixed(0), EmptySlot(1));
        h.Time.Advance(Tick);
        Assert.Equal(0, h.Changes);
        Assert.Equal([1], h.Reads.Single().ToArray());

        h.Replace(Card(1));
        h.Time.Advance(Tick);
        Assert.Equal(1, h.Changes);

        // After the refresh, the app hands over the new snapshot: no further change.
        h.Poller.Watch(Layouts.Snapshot([.. h.Windows]));
        h.Time.Advance(Tick);
        Assert.Equal(1, h.Changes);
    }

    [Fact]
    public void Card_removed_raises_a_change()
    {
        var h = new Harness(Fixed(0), Card(1));
        h.Replace(EmptySlot(1));
        h.Time.Advance(Tick);
        Assert.Equal(1, h.Changes);
    }

    [Fact]
    public void Slot_stays_watched_after_a_card_is_inserted_even_if_not_yet_reported_removable()
    {
        var slot = EmptySlot(1);
        var h = new Harness(Fixed(0), slot);

        // The refresh after insertion: Windows doesn't list the card as removable yet.
        var card = new Disk { Id = slot.Id, Number = 1, Bus = BusType.Usb, Size = 1 * GB, Style = PartitionStyle.Mbr };
        h.Replace(card);
        h.Poller.Watch(Layouts.Snapshot(Fixed(0), card));
        Assert.Equal([1], h.Poller.Watched.ToArray());

        // The card is removed: found without a refresh button.
        h.Replace(slot);
        h.Time.Advance(Tick);
        Assert.Equal(1, h.Changes);
    }

    [Fact]
    public void Disconnected_disk_is_no_longer_watched()
    {
        var slot = EmptySlot(1);
        var h = new Harness(Fixed(0), slot);
        h.Poller.Watch(Layouts.Snapshot(Fixed(0)));
        Assert.Empty(h.Poller.Watched);
    }
    [Fact]
    public void Nothing_to_watch_reads_nothing()
    {
        var h = new Harness(Fixed(0));
        h.Time.Advance(Tick * 5);
        Assert.Empty(h.Reads);
    }

    [Fact]
    public void Paused_reads_nothing_and_finds_the_change_afterwards()
    {
        var h = new Harness(Fixed(0), EmptySlot(1));
        h.Poller.IsPaused = true;
        h.Replace(Card(1));
        h.Time.Advance(Tick * 3);
        Assert.Empty(h.Reads);
        Assert.Equal(0, h.Changes);

        h.Poller.IsPaused = false;
        h.Time.Advance(Tick);
        Assert.Equal(1, h.Changes);
    }

    [Fact]
    public void Failed_check_is_ignored()
    {
        var h = new Harness(Fixed(0), EmptySlot(1)) { Override = () => throw new InvalidOperationException("WMI busy") };
        h.Time.Advance(Tick);
        Assert.Equal(0, h.Changes);

        h.Override = null;
        h.Replace(Card(1));
        h.Time.Advance(Tick);
        Assert.Equal(1, h.Changes);
    }

    [Fact]
    public async Task A_slow_check_makes_the_next_ones_skip()
    {
        var h = new Harness(Fixed(0), EmptySlot(1));
        var started = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        h.Override = () =>
        {
            started.Set();
            release.Wait();
            return [DiskMediaState.Of(EmptySlot(1))];
        };

        var slow = Task.Run(h.Poller.Check);
        Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
        h.Poller.Check();
        h.Poller.Check();
        Assert.Single(h.Reads);

        release.Set();
        await slow.WaitAsync(TimeSpan.FromSeconds(5));
        h.Override = null;
        h.Poller.Check();
        Assert.Equal(2, h.Reads.Count);
    }

    [Fact]
    public void Disposed_poller_reads_nothing()
    {
        var h = new Harness(Fixed(0), EmptySlot(1));
        h.Poller.Dispose();
        h.Poller.Check();
        Assert.Empty(h.Reads);
    }
}
