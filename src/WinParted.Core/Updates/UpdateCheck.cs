using System.Text.Json;

namespace WinParted.Core.Updates;

/// <summary>A release as listed by the GitHub Releases API.</summary>
public sealed record ReleaseInfo(string Tag, bool IsPrerelease, bool IsDraft, string PageUrl);

/// <summary>A release worth announcing: its version, and whether it is a pre-release.</summary>
public sealed record AvailableUpdate(Version Version, bool IsPrerelease, string PageUrl)
{
    /// <summary>The version as shown to users: three parts, e.g. "0.3.0".</summary>
    public string VersionText => Version.ToString(3);
}

/// <summary>
/// Decides whether a newer release should be announced (update-notice spec; design D5). Pure logic;
/// the request to GitHub is made by the app.
/// </summary>
public static class UpdateCheck
{
    /// <summary>
    /// The highest eligible release newer than <paramref name="current"/>, or null. Drafts never count,
    /// pre-releases only when <paramref name="includePrereleases"/> is set, and tags that aren't a
    /// three-part version (optionally prefixed with "v") are ignored.
    /// </summary>
    public static AvailableUpdate? FindNewer(IEnumerable<ReleaseInfo> releases, Version current, bool includePrereleases)
    {
        var running = Normalize(current);
        AvailableUpdate? best = null;
        foreach (var release in releases)
        {
            if (release.IsDraft || (release.IsPrerelease && !includePrereleases) || TryParseVersion(release.Tag) is not { } version)
            {
                continue;
            }

            if (version > running && (best is null || version > best.Version))
            {
                best = new AvailableUpdate(version, release.IsPrerelease, release.PageUrl);
            }
        }

        return best;
    }

    /// <summary>"v1.2.3" or "1.2.3" → 1.2.3; anything else (suffixes, two or four parts) → null.</summary>
    public static Version? TryParseVersion(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return null;
        }

        var text = tag.Trim();
        if (text.StartsWith('v') || text.StartsWith('V'))
        {
            text = text[1..];
        }

        var parts = text.Split('.');
        if (parts.Length != 3)
        {
            return null;
        }

        var numbers = new int[3];
        for (var i = 0; i < 3; i++)
        {
            if (parts[i].Length == 0 || !parts[i].All(char.IsAsciiDigit) || !int.TryParse(parts[i], out numbers[i]))
            {
                return null;
            }
        }

        return new Version(numbers[0], numbers[1], numbers[2]);
    }

    /// <summary>
    /// Reads the JSON array returned by <c>GET /repos/{owner}/{repo}/releases</c>. Entries missing
    /// a tag or page URL are skipped. Throws <see cref="JsonException"/> when the text isn't such an array.
    /// </summary>
    public static IReadOnlyList<ReleaseInfo> ParseReleases(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("Expected a JSON array of releases.");
        }

        var releases = new List<ReleaseInfo>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object
                && String(item, "tag_name") is { } tag
                && String(item, "html_url") is { } url)
            {
                releases.Add(new ReleaseInfo(tag, Bool(item, "prerelease"), Bool(item, "draft"), url));
            }
        }

        return releases;
    }

    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));

    private static string? String(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool Bool(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
