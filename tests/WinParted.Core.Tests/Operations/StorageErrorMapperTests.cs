using WinParted.Core.Devices;
using WinParted.Core.Operations;
using WinParted.Core.Text;
using Microsoft.Extensions.Time.Testing;

namespace WinParted.Core.Tests.Operations;

/// <summary>Task 8.2: every code observed in docs/spikes/smp-behavior.md, plus the unknown fallback.</summary>
public class StorageErrorMapperTests
{
    [Theory]
    [InlineData(1u, "CreatePartition", false, StorageErrorKind.NotSupported)]
    [InlineData(1u, "Format", true, StorageErrorKind.FullFormatNotSupported)]
    [InlineData(1u, "Format", false, StorageErrorKind.NotSupported)]
    [InlineData(5u, "SetFileSystemLabel", false, StorageErrorKind.InvalidParameter)]
    [InlineData(4097u, "Resize", false, StorageErrorKind.SizeNotSupported)]
    [InlineData(40000u, "CreatePartition", false, StorageErrorKind.NotEnoughSpace)]
    [InlineData(40001u, "Clear", false, StorageErrorKind.AccessDenied)]
    [InlineData(41001u, "Initialize", false, StorageErrorKind.AlreadyInitialized)]
    [InlineData(41002u, "CreatePartition", false, StorageErrorKind.ReadOnly)]
    [InlineData(41003u, "CreatePartition", false, StorageErrorKind.PartitionLimit)]
    [InlineData(2u, "Clear", false, StorageErrorKind.Unknown)]
    [InlineData(99999u, "Clear", false, StorageErrorKind.Unknown)]
    public void Classifies_observed_codes(uint code, string method, bool full, StorageErrorKind expected)
    {
        Assert.Equal(expected, StorageErrorMapper.Classify(code, method, full));
    }

    [Fact]
    public void Validation_failures_changed_nothing()
    {
        var error = StorageErrorMapper.Map(40000, "CreatePartition");

        Assert.False(error.MayHaveChanged);
        Assert.Equal(40000u, error.Code);
        Assert.Contains("Nothing was changed", error.Message, StringComparison.Ordinal);
        Assert.Contains("CreatePartition returned 40000", error.Details, StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_failure_may_have_changed_the_disk()
    {
        var error = StorageErrorMapper.Map(2, "Clear");

        Assert.True(error.MayHaveChanged);
        Assert.Contains("may have been partly changed", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Extended_status_message_goes_to_details()
    {
        var error = StorageErrorMapper.Map(4097, "Resize", "The partition is already the requested size.");

        Assert.Contains("Message: The partition is already the requested size.", error.Details, StringComparison.Ordinal);
        Assert.DoesNotContain("requested size", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Drive_letter_in_use_changed_nothing_and_names_both_letters()
    {
        var error = StorageErrorMapper.DriveLetterInUse('X', 'E', 42002, "AddAccessPath returned 42002");

        Assert.False(error.MayHaveChanged);
        Assert.Equal("Drive letter X: is already in use by another drive. Nothing was changed. The volume is still E:.", error.Message);
        Assert.Equal("Choose another letter.", error.Advice);
        Assert.Null(error.Changed);
        Assert.False(StorageErrorMapper.Map(42002, "AddAccessPath").MayHaveChanged);
    }

    [Fact]
    public void Drive_letter_in_use_without_a_current_letter()
    {
        var error = StorageErrorMapper.DriveLetterInUse('X', null, 42002, "AddAccessPath returned 42002");

        Assert.Equal("Drive letter X: is already in use by another drive. Nothing was changed.", error.Message);
    }

    [Fact]
    public void Created_but_letter_failed_says_the_disk_changed()
    {
        var error = StorageErrorMapper.CreatedButLetterFailed('D', 42002, "AddAccessPath after CreatePartition");

        Assert.True(error.MayHaveChanged);
        Assert.Equal("The partition was created and formatted, but drive letter D: is already in use by another drive.", error.Message);
        Assert.Equal("Yes. The partition was created and formatted, without a drive letter.", error.Changed);
        Assert.Equal("Use Change drive letter to give it a letter.", error.Advice);
    }
    public static TheoryData<StorageErrorKind> AllKinds => [.. Enum.GetValues<StorageErrorKind>()];

    [Theory]
    [MemberData(nameof(AllKinds))]
    public void Every_kind_has_text(StorageErrorKind kind)
    {
        Assert.True(Texts.Exists($"Error.{kind}"), $"Missing Error.{kind}");
        Assert.False(string.IsNullOrWhiteSpace(StorageErrorMapper.Message(kind)));
    }
}

/// <summary>Task 10.1 (unit part): a burst of notifications becomes one refresh.</summary>
public class ChangeDebouncerTests
{
    [Fact]
    public void Burst_becomes_one_refresh()
    {
        var time = new FakeTimeProvider();
        using var debouncer = new ChangeDebouncer(time);
        var fired = 0;
        debouncer.Settled += (_, _) => fired++;

        debouncer.Signal();
        time.Advance(TimeSpan.FromMilliseconds(200));
        debouncer.Signal();
        time.Advance(TimeSpan.FromMilliseconds(200));
        debouncer.Signal();
        Assert.Equal(0, fired);

        time.Advance(TimeSpan.FromMilliseconds(499));
        Assert.Equal(0, fired);
        time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(1, fired);

        time.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(1, fired);
    }

    [Fact]
    public void Separate_events_refresh_separately()
    {
        var time = new FakeTimeProvider();
        using var debouncer = new ChangeDebouncer(time);
        var fired = 0;
        debouncer.Settled += (_, _) => fired++;

        debouncer.Signal();
        time.Advance(TimeSpan.FromSeconds(1));
        debouncer.Signal();
        time.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(2, fired);
    }

    [Fact]
    public void Attached_source_signals_the_debouncer()
    {
        var time = new FakeTimeProvider();
        using var debouncer = new ChangeDebouncer(time);
        var source = new FakeSource();
        debouncer.Attach(source);
        var fired = 0;
        debouncer.Settled += (_, _) => fired++;

        source.Raise();
        source.Raise();
        time.Advance(ChangeDebouncer.DefaultDelay);

        Assert.Equal(1, fired);
    }

    private sealed class FakeSource : IDeviceChangeSource
    {
        public event EventHandler? Changed;

        public void Raise() => Changed?.Invoke(this, EventArgs.Empty);

        public void Start()
        {
        }

        public void Dispose()
        {
        }
    }
}
