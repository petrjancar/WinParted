using System.Globalization;
using System.Resources;
using WinParted.Core.Actions;
using WinParted.Core.Model;
using WinParted.Core.Protection;

namespace WinParted.Core.Text;

/// <summary>Access to all user-facing strings owned by Core (English only in the MVP).</summary>
public static class Texts
{
    private static readonly ResourceManager Resources = new("WinParted.Core.Resources.Texts", typeof(Texts).Assembly);

    public static string Get(string key) =>
        Resources.GetString(key, CultureInfo.CurrentUICulture)
        ?? throw new KeyNotFoundException($"Missing text resource '{key}'.");

    public static bool Exists(string key) => Resources.GetString(key, CultureInfo.InvariantCulture) is not null;

    public static string Format(Reason reason) =>
        string.Format(CultureInfo.CurrentCulture, Get(ReasonKey(reason.Code)), [.. reason.Args]);

    public static string Role(ProtectionRole role) => Get(RoleKey(role));

    public static string ReasonKey(ReasonCode code) => $"Reason.{code}";

    public static string RoleKey(ProtectionRole role) => $"Role.{role}";

    public static string FileSystem(FileSystemKind fs, string? reportedName = null) => fs switch
    {
        FileSystemKind.Other when !string.IsNullOrWhiteSpace(reportedName) => reportedName,
        _ => Get($"FileSystem.{fs}"),
    };
}
