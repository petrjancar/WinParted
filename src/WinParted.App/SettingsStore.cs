using WinParted.Core.Settings;

namespace WinParted.App;

/// <summary>
/// settings.json in <see cref="App.DataFolder"/> (design D4): read once at start, saved on every change.
/// Saving needs administrator rights; without them the change is kept for this run only and the
/// failure is logged, never shown.
/// </summary>
public static class SettingsStore
{
    private static readonly string FilePath = Path.Combine(App.DataFolder, "settings.json");

    public static AppSettings Current { get; private set; } = Load();

    public static void Save(AppSettings settings)
    {
        Current = settings;
        var temp = FilePath + ".tmp";
        try
        {
            Directory.CreateDirectory(App.DataFolder);
            File.WriteAllText(temp, settings.Serialize());
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            App.Log($"Settings not saved: {ex.Message}");
            try
            {
                File.Delete(temp);
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static AppSettings Load()
    {
        try
        {
            return File.Exists(FilePath) ? AppSettings.Parse(File.ReadAllText(FilePath)) : AppSettings.Default;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            App.Log($"Settings not read, using defaults: {ex.Message}");
            return AppSettings.Default;
        }
    }
}
