using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;
using WinParted.Core.Settings;
using WinParted.Core.Updates;

namespace WinParted.App;

/// <summary>
/// The update check (update-notice spec; design D5): one HTTPS request to the GitHub Releases API.
/// Every failure is logged and otherwise ignored.
/// </summary>
public static class UpdateService
{
    public const string RepositoryUrl = "https://github.com/petrjancar/WinParted";

    private static readonly Uri ReleasesApi = new("https://api.github.com/repos/petrjancar/WinParted/releases?per_page=30");

    /// <summary>The running version (from &lt;Version&gt; in Directory.Build.props).</summary>
    public static Version CurrentVersion { get; } = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    /// <summary>Developer option <c>--pretend-version x.y.z</c>: compare against this version instead (for testing the notice).</summary>
    public static Version? PretendVersion { get; set; }

    public static async Task<AvailableUpdate?> CheckAsync(AppSettings settings)
    {
        if (!settings.CheckForUpdates)
        {
            App.Log("Update check skipped (turned off).");
            return null;
        }

        var current = PretendVersion ?? CurrentVersion;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesApi);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("WinParted", CurrentVersion.ToString(3)));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            using var response = await http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                App.Log($"Update check failed: GitHub answered {(int)response.StatusCode} {response.ReasonPhrase}.");
                return null;
            }

            var releases = UpdateCheck.ParseReleases(await response.Content.ReadAsStringAsync());
            var update = UpdateCheck.FindNewer(releases, current, settings.IncludePrereleases);
            App.Log(update is null
                ? $"Update check: {current.ToString(3)} is up to date ({releases.Count} releases, pre-releases {(settings.IncludePrereleases ? "included" : "ignored")})."
                : $"Update check: {update.VersionText}{(update.IsPrerelease ? " (pre-release)" : "")} is available.");
            return update;
        }
        catch (Exception ex)
        {
            App.Log($"Update check failed: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Opens a web page in the user's default browser. WinParted runs elevated, so the URL goes through
    /// Explorer, which opens the browser without administrator rights.
    /// </summary>
    public static void OpenInBrowser(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{uri.AbsoluteUri}\"") { UseShellExecute = false });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            App.Log($"Couldn't open {uri}: {ex.Message}");
        }
    }
}
