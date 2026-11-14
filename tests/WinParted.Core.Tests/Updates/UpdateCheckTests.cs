using System.Text.Json;
using WinParted.Core.Updates;

namespace WinParted.Core.Tests.Updates;

/// <summary>Which release to announce (task 4.1, update-notice spec, design D5).</summary>
public class UpdateCheckTests
{
    private static readonly Version Current = new(0, 2, 0);

    private static ReleaseInfo Release(string tag, bool pre = false, bool draft = false) =>
        new(tag, pre, draft, $"https://github.com/petrjancar/WinParted/releases/tag/{tag}");

    [Fact]
    public void Newer_full_release_is_found()
    {
        var update = UpdateCheck.FindNewer([Release("v0.3.0"), Release("v0.2.0")], Current, includePrereleases: false);

        Assert.NotNull(update);
        Assert.Equal("0.3.0", update.VersionText);
        Assert.False(update.IsPrerelease);
        Assert.Equal("https://github.com/petrjancar/WinParted/releases/tag/v0.3.0", update.PageUrl);
    }

    [Theory]
    [InlineData("v0.2.0")]
    [InlineData("v0.1.0")]
    [InlineData("0.1.9")]
    public void Same_or_older_release_is_ignored(string tag)
    {
        Assert.Null(UpdateCheck.FindNewer([Release(tag)], Current, includePrereleases: true));
    }

    [Fact]
    public void Drafts_never_count()
    {
        Assert.Null(UpdateCheck.FindNewer([Release("v0.9.0", draft: true)], Current, includePrereleases: true));
    }

    [Fact]
    public void Prerelease_is_ignored_by_default()
    {
        Assert.Null(UpdateCheck.FindNewer([Release("v0.3.0", pre: true)], Current, includePrereleases: false));
    }

    [Fact]
    public void Prerelease_counts_when_included()
    {
        var update = UpdateCheck.FindNewer([Release("v0.3.0", pre: true)], Current, includePrereleases: true);

        Assert.NotNull(update);
        Assert.True(update.IsPrerelease);
    }

    [Fact]
    public void Newer_full_release_is_found_even_when_a_newer_prerelease_is_skipped()
    {
        var releases = new[] { Release("v0.4.0", pre: true), Release("v0.3.0") };

        Assert.Equal("0.3.0", UpdateCheck.FindNewer(releases, Current, includePrereleases: false)?.VersionText);
        Assert.Equal("0.4.0", UpdateCheck.FindNewer(releases, Current, includePrereleases: true)?.VersionText);
    }

    [Fact]
    public void Highest_version_wins_regardless_of_order()
    {
        var releases = new[] { Release("v0.3.0"), Release("v0.10.0"), Release("v0.9.0") };

        Assert.Equal("0.10.0", UpdateCheck.FindNewer(releases, Current, includePrereleases: false)?.VersionText);
    }

    [Fact]
    public void Versions_compare_numerically()
    {
        Assert.NotNull(UpdateCheck.FindNewer([Release("v0.10.0")], new Version(0, 9, 0), includePrereleases: false));
    }

    [Fact]
    public void Four_part_running_version_is_compared_by_its_first_three_parts()
    {
        Assert.Null(UpdateCheck.FindNewer([Release("v0.2.0")], new Version(0, 2, 0, 0), includePrereleases: false));
        Assert.NotNull(UpdateCheck.FindNewer([Release("v0.2.1")], new Version(0, 2, 0, 0), includePrereleases: false));
    }

    [Theory]
    [InlineData("v1.2.3", 1, 2, 3)]
    [InlineData("1.2.3", 1, 2, 3)]
    [InlineData("V0.10.0", 0, 10, 0)]
    [InlineData(" v2.0.1 ", 2, 0, 1)]
    public void Tags_with_or_without_v_parse(string tag, int major, int minor, int patch)
    {
        Assert.Equal(new Version(major, minor, patch), UpdateCheck.TryParseVersion(tag));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("v1.2")]
    [InlineData("v1.2.3.4")]
    [InlineData("v1.2.3-beta")]
    [InlineData("v1..3")]
    [InlineData("v-1.2.3")]
    [InlineData("v1.2.x")]
    public void Malformed_tags_are_ignored(string? tag)
    {
        Assert.Null(UpdateCheck.TryParseVersion(tag));
        Assert.Null(UpdateCheck.FindNewer([Release(tag ?? "")], Current, includePrereleases: true));
    }

    [Fact]
    public void Empty_list_has_no_update()
    {
        Assert.Null(UpdateCheck.FindNewer([], Current, includePrereleases: true));
    }

    [Fact]
    public void GitHub_release_list_is_parsed()
    {
        const string json = """
            [
              { "tag_name": "v0.3.0", "prerelease": false, "draft": false, "html_url": "https://github.com/petrjancar/WinParted/releases/tag/v0.3.0", "name": "WinParted 0.3.0" },
              { "tag_name": "v0.4.0", "prerelease": true, "draft": false, "html_url": "https://github.com/petrjancar/WinParted/releases/tag/v0.4.0" },
              { "tag_name": "v0.5.0", "prerelease": false, "draft": true, "html_url": "https://github.com/petrjancar/WinParted/releases/tag/untagged-1" },
              { "tag_name": null, "html_url": "https://example.invalid" },
              { "tag_name": "v0.6.0" },
              42
            ]
            """;

        var releases = UpdateCheck.ParseReleases(json);

        Assert.Equal(3, releases.Count);
        Assert.Equal(new ReleaseInfo("v0.3.0", false, false, "https://github.com/petrjancar/WinParted/releases/tag/v0.3.0"), releases[0]);
        Assert.True(releases[1].IsPrerelease);
        Assert.True(releases[2].IsDraft);
    }

    [Theory]
    [InlineData("""{ "message": "API rate limit exceeded" }""")]
    [InlineData("not json")]
    public void Unexpected_response_throws_json_exception(string json)
    {
        Assert.ThrowsAny<JsonException>(() => UpdateCheck.ParseReleases(json));
    }
}
