using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinParted.Core.Settings;

/// <summary>
/// User settings saved in settings.json (update-notice spec, "Update settings"; design D4).
/// A missing, empty or unreadable file means the defaults; unknown fields are ignored.
/// </summary>
public sealed record AppSettings
{
    /// <summary>Check GitHub for a newer release when WinParted starts.</summary>
    public bool CheckForUpdates { get; init; } = true;

    /// <summary>Count releases marked as pre-releases on GitHub.</summary>
    public bool IncludePrereleases { get; init; }

    public static AppSettings Default { get; } = new();

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>Reads settings.json text. Never throws: anything that isn't valid settings gives the defaults.</summary>
    public static AppSettings Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Default;
        }

        try
        {
            return JsonSerializer.Deserialize<AppSettings>(json, Options) ?? Default;
        }
        catch (JsonException)
        {
            return Default;
        }
    }

    public string Serialize() => JsonSerializer.Serialize(this, Options);
}
