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
