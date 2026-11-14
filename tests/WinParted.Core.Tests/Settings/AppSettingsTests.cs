using WinParted.Core.Settings;

namespace WinParted.Core.Tests.Settings;

/// <summary>Settings file parsing (task 3.1, design D4).</summary>
public class AppSettingsTests
{
    [Fact]
    public void Defaults_check_for_updates_without_prereleases()
    {
        Assert.True(AppSettings.Default.CheckForUpdates);
        Assert.False(AppSettings.Default.IncludePrereleases);
    }

    [Fact]
    public void Round_trip_keeps_values()
    {
        var settings = new AppSettings { CheckForUpdates = false, IncludePrereleases = true };

        Assert.Equal(settings, AppSettings.Parse(settings.Serialize()));
    }

    [Fact]
    public void Serialized_form_is_readable_json()
    {
        var json = AppSettings.Default.Serialize();

        Assert.Contains("\"checkForUpdates\": true", json);
        Assert.Contains("\"includePrereleases\": false", json);
    }

    [Fact]
    public void Unknown_fields_are_ignored()
    {
        var settings = AppSettings.Parse("""{ "checkForUpdates": false, "theme": "dark" }""");

        Assert.False(settings.CheckForUpdates);
        Assert.False(settings.IncludePrereleases);
    }

    [Fact]
    public void Missing_field_keeps_its_default()
    {
        Assert.True(AppSettings.Parse("""{ "includePrereleases": true }""").CheckForUpdates);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{")]
    [InlineData("not json")]
    [InlineData("[1, 2]")]
    [InlineData("""{ "checkForUpdates": "maybe" }""")]
    [InlineData("null")]
    public void Missing_empty_or_corrupt_input_gives_defaults(string? json)
    {
        Assert.Equal(AppSettings.Default, AppSettings.Parse(json));
    }
}
