using System.Globalization;
using WinParted.Core.Actions;
using WinParted.Core.Model;
using WinParted.Core.Presentation;
using WinParted.Core.Samples;
using WinParted.Core.Tests.Builders;
using static WinParted.Core.Tests.Actions.AvailabilityAssert;

namespace WinParted.Core.Tests.Actions;

/// <summary>Change empty-card-reader-slots: a drive without media (tasks 2.1, 2.2, 2.4).</summary>
public class NoMediaTests
{
    public NoMediaTests() => CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");

    /// <summary>What Windows reports for an empty card reader slot (design.md, Context).</summary>
    private static Disk EmptySlot(bool readOnly = false, bool? spaces = false) =>
        new DiskBuilder(1, PartitionStyle.Uninitialized)
        {
            FriendlyName = "Generic STORAGE DEVICE",
            Bus = BusType.Usb,
            IsOffline = true,
            IsRemovableMedia = true,
            IsReadOnly = readOnly,
            IsStorageSpacesMember = spaces,
            HasNoMedia = true,
        }.Build(totalSize: 0);

    private static ActionKind[] DiskActions(Disk disk) =>
        [.. ActionAvailability.RelevantActions(Layouts.Snapshot(disk), new DiskTarget(disk.Id))];

    [Theory]
    [InlineData(new ushort[] { 0xD012 }, 0UL, true)]
    [InlineData(new ushort[] { 0xD012 }, 64UL, true)]
    [InlineData(new ushort[] { 2 }, 0UL, true)]
    [InlineData(new ushort[] { 2 }, 64UL, false)]
    [InlineData(null, 64UL, false)]
    public void No_media_from_operational_status_or_size(ushort[]? status, ulong size, bool expected) =>
        Assert.Equal(expected, MediaStatus.IsMissing(status, size));
    [Fact]
    public void Drive_without_media_lists_initialize_and_clear_disk_both_disabled()
    {
        var disk = EmptySlot();

        Assert.Equal([ActionKind.Initialize, ActionKind.ClearDisk], DiskActions(disk));
        Disabled(Evaluate(Layouts.Snapshot(disk), ActionKind.Initialize, disk), ReasonCode.NoMedia);
        Disabled(Evaluate(Layouts.Snapshot(disk), ActionKind.ClearDisk, disk), ReasonCode.NoMedia);
        Assert.Equal("There's no card or disk in this drive. Insert one first.", Reason.Of(ReasonCode.NoMedia).Text);
    }

    [Fact]
    public void No_media_wins_over_read_only_offline_and_managed()
    {
        foreach (var disk in new[] { EmptySlot(readOnly: true), EmptySlot(spaces: true), EmptySlot(spaces: null) })
        {
            Assert.Equal([ActionKind.Initialize, ActionKind.ClearDisk], DiskActions(disk));
            foreach (var kind in new[] { ActionKind.Initialize, ActionKind.ClearDisk, ActionKind.BringOnline, ActionKind.TakeOffline, ActionKind.ClearReadOnly })
            {
                Disabled(Evaluate(Layouts.Snapshot(disk), kind, disk), ReasonCode.NoMedia);
            }
        }
    }

    [Fact]
    public void Busy_still_wins()
    {
        var disk = EmptySlot();
        Disabled(Evaluate(Layouts.Snapshot(disk), ActionKind.Initialize, disk, isBusy: true), ReasonCode.OperationInProgress);
    }

    [Fact]
    public void Empty_card_reader_slot_in_the_list_and_header()
    {
        var view = Presenter.Disk(EmptySlot());

        Assert.Equal(DiskState.NoMedia, view.State);
        Assert.Equal("", view.SizeText);
        Assert.Equal("USB · No media", view.Meta);
        Assert.Empty(view.Segments);
        Assert.Equal(["No media"], view.Chips.Select(c => c.Text));
        Assert.Equal(ChipTone.Neutral, view.Chips[0].Tone);
        Assert.Equal(["USB", "No media"], view.HeaderChips.Select(c => c.Text));
        Assert.Null(view.OfflineReason);
        Assert.DoesNotContain("Offline", view.AutomationName, StringComparison.Ordinal);
        Assert.Contains("No media", view.AutomationName, StringComparison.Ordinal);
    }

    [Fact]
    public void Empty_slot_selected_shows_bus_status_serial_and_location()
    {
        var disk = new Disk
        {
            Id = new DiskId("slot"),
            Number = 1,
            Bus = BusType.Usb,
            Size = 0,
            LogicalSectorSize = 0,
            IsOffline = true,
            HasNoMedia = true,
            SerialNumber = "000000000820",
            Location = "USB card reader",
        };
        var snapshot = Layouts.Snapshot(disk);

        var details = Presenter.Details(snapshot, new WinParted.Core.Selections.Selection(disk.Id, null))!;

        Assert.Equal(["Bus type", "Status", "Serial number", "Location"], details.Rows.Select(r => r.Label));
        Assert.Equal("No media", details.Rows[1].Value);
    }

    [Fact]
    public void Card_reader_connected_says_no_media_instead_of_0_bytes()
    {
        var slot = EmptySlot();
        var messages = ChangeMessages.Between(Layouts.Snapshot(), Layouts.Snapshot(slot), null);
        Assert.Equal(["USB disk connected: Generic STORAGE DEVICE (no media)"], messages);
    }

    [Fact]
    public void Card_inserted_into_the_slot_is_not_a_new_disk()
    {
        var slot = EmptySlot();
        var withCard = new Disk { Id = slot.Id, Number = slot.Number, Bus = BusType.Usb, Size = 62 * DiskBuilder.GB, Style = PartitionStyle.Mbr };
        Assert.Empty(ChangeMessages.Between(Layouts.Snapshot(slot), Layouts.Snapshot(withCard), slot.Id, slot.Id));
        Assert.Empty(ChangeMessages.Between(Layouts.Snapshot(withCard), Layouts.Snapshot(slot), slot.Id, slot.Id));
    }
    [Fact]
    public void Sample_mode_has_an_empty_slot_and_doesnt_offer_its_letter()
    {
        var snapshot = SampleStorageProvider.Snapshot();
        var slot = snapshot.Disks.Single(d => d.Number == 7);

        Assert.True(slot.HasNoMedia);
        Assert.Equal(BusType.Usb, slot.Bus);
        Assert.DoesNotContain('I', snapshot.Facts.FreeDriveLetters);
        Assert.Equal('J', snapshot.Facts.FreeDriveLetters.First());
    }
}
