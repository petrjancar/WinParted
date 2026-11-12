using WinParted.Core.Actions;
using WinParted.Core.Model;
using WinParted.Core.Protection;
using WinParted.Core.Tests.Builders;
using WinParted.Core.Text;
using static WinParted.Core.Tests.Builders.DiskBuilder;

namespace WinParted.Core.Tests.Text;

/// <summary>Every code resolves to an English string (task 6.3).</summary>
public class TextsTests
{
    public static TheoryData<ReasonCode> AllReasonCodes => [.. Enum.GetValues<ReasonCode>()];

    public static TheoryData<ProtectionRole> AllRoles => [.. Enum.GetValues<ProtectionRole>()];

    public static TheoryData<FileSystemKind> AllFileSystems => [.. Enum.GetValues<FileSystemKind>()];

    [Theory]
    [MemberData(nameof(AllReasonCodes))]
    public void Every_reason_code_has_text(ReasonCode code)
    {
        Assert.True(Texts.Exists(Texts.ReasonKey(code)), $"Missing Reason.{code}");

        // Formatting with placeholder arguments must not throw.
        var text = Reason.Of(code, "X", "Y").Text;
        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.DoesNotContain("{", text, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(AllRoles))]
    public void Every_protection_role_has_text(ProtectionRole role)
    {
        Assert.False(string.IsNullOrWhiteSpace(Texts.Role(role)));
    }

    [Theory]
    [MemberData(nameof(AllFileSystems))]
    public void Every_file_system_has_a_name(FileSystemKind fs)
    {
        Assert.False(string.IsNullOrWhiteSpace(Texts.FileSystem(fs)));
    }

    [Fact]
    public void Reported_name_is_used_for_unknown_file_systems()
    {
        Assert.Equal("ext4", Texts.FileSystem(FileSystemKind.Other, "ext4"));
    }
}

public class DisplayNamesTests
{
    [Fact]
    public void Titles_follow_the_brief()
    {
        var w11 = Layouts.Windows11Default();
        Assert.Equal("EFI System", DisplayNames.ForPartition(w11.Partitions[0]));
        Assert.Equal("Microsoft Reserved", DisplayNames.ForPartition(w11.Partitions[1]));
        Assert.Equal("C:", DisplayNames.ForPartition(w11.Partitions[2]));
        Assert.Equal("Recovery", DisplayNames.ForPartition(w11.Partitions[3]));

        Assert.Equal("BACKUP (E:)", DisplayNames.ForPartition(Layouts.UsbStick().Partitions[0]));
        Assert.Equal("Unknown filesystem", DisplayNames.ForPartition(Layouts.DataDiskWithLinux().Partitions[1]));
        Assert.Equal("Unformatted", DisplayNames.ForPartition(new DiskBuilder(1).Part(GB).Build().Partitions[0]));
        Assert.Equal("System Reserved", DisplayNames.ForPartition(Layouts.LegacyMbr().Partitions[0]));
    }
}
