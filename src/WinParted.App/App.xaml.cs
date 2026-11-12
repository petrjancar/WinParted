using System.Security.Principal;
using Microsoft.UI.Xaml;
using WinParted.Core.Devices;
using WinParted.Core.Operations;
using WinParted.Core.Samples;
using WinParted.Storage;

namespace WinParted.App;

public partial class App : Application
{
    private Window? _window;
    private IDeviceChangeSource? _deviceChanges;
    private ChangeDebouncer? _debouncer;

    public App()
    {
        InitializeComponent();
        if (Option("--theme") is { } theme)
        {
            RequestedTheme = theme.Equals("dark", StringComparison.OrdinalIgnoreCase) ? ApplicationTheme.Dark : ApplicationTheme.Light;
        }
        UnhandledException += (_, e) => Log($"Unhandled: {e.Exception}");
    }

    /// <summary>
    /// Diagnostic log: %LOCALAPPDATA%\WinParted\winparted.log, or the file named by the
    /// WINPARTED_LOG environment variable.
    /// </summary>
    public static void Log(string message)
    {
        try
        {
            var path = Environment.GetEnvironmentVariable("WINPARTED_LOG")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinParted", "winparted.log");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, $"{DateTime.Now:O} {message}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Run with <c>--sample</c> to show the design's sample disks instead of real ones.</summary>
    public static bool IsSampleMode => Environment.GetCommandLineArgs().Skip(1).Any(a => a.Equals("--sample", StringComparison.OrdinalIgnoreCase));

    /// <summary>Developer options: <c>--theme dark|light</c>, <c>--select disk[:segment]</c>.</summary>
    private static string? Option(string name)
    {
        var args = Environment.GetCommandLineArgs();
        var i = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    public static bool IsElevated => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            Launch();
        }
        catch (Exception ex)
        {
            Log($"Launch failed: {ex}");
            throw;
        }
    }

    private void Launch()
    {
        if (!IsElevated && !IsSampleMode)
        {
            _window = new MainWindow(viewModel: null);
            _window.Activate();
            return;
        }

        IStorageProvider provider = IsSampleMode ? new SampleStorageProvider() : new SmpStorageProvider();
        var viewModel = new MainViewModel(provider, IsSampleMode);
        if (Option("--select") is { } select)
        {
            var parts = select.Split(':');
            viewModel.InitialSelection = (int.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
                parts.Length > 1 ? int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture) : null);
        }
        var window = new MainWindow(viewModel);
        if (Option("--open") is { } open && Enum.TryParse<Core.Actions.ActionKind>(open, ignoreCase: true, out var kind))
        {
            window.OpenOnStart = kind;
        }
        _window = window;
        _window.Activate();

        if (!IsSampleMode)
        {
            // Hot-plug (design D9): debounced change notifications trigger a full refresh on the UI thread.
            _debouncer = new ChangeDebouncer();
            _debouncer.Settled += (_, _) => window.DispatcherQueue.TryEnqueue(() => _ = viewModel.RefreshAsync());
            var source = new SmpDeviceChangeSource();
            _debouncer.Attach(source);
            source.Start();
            _deviceChanges = source;
        }

        window.Closed += (_, _) =>
        {
            _deviceChanges?.Dispose();
            _debouncer?.Dispose();
            (provider as IDisposable)?.Dispose();
        };
    }
}
