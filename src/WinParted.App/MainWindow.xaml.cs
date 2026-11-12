using System.Diagnostics;
using System.Reflection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using WinParted.App.Dialogs;
using WinParted.Core.Actions;
using WinParted.Core.Model;
using WinParted.Core.Operations;
using WinParted.Core.Presentation;
using WinParted.Core.Selections;

namespace WinParted.App;

public sealed partial class MainWindow : Window
{
    /// <param name="viewModel">Null when WinParted runs without administrator rights.</param>
    public MainWindow(MainViewModel? viewModel)
    {
        Vm = viewModel!;
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppTitleBar.Loaded += (_, _) => UpdateTitleBarLayout();
        AppTitleBar.SizeChanged += (_, _) => UpdateTitleBarLayout();

        // Caption buttons (minimize, maximize, close) follow the app's theme, not the Windows one, so
        // they stay readable when the two differ (e.g. --theme dark on a light Windows).
        if (Content is FrameworkElement root)
        {
            void MatchTheme() => AppWindow.TitleBar.PreferredTheme =
                root.ActualTheme == ElementTheme.Dark ? TitleBarTheme.Dark : TitleBarTheme.Light;
            MatchTheme();
            root.ActualThemeChanged += (_, _) => MatchTheme();
        }

        // Design: minimum 1000 x 640 effective pixels. AppWindow works in physical pixels, so scale
        // by the window's DPI (e.g. 150% display scaling = 1.5).
        var scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = (int)(1000 * scale);
            presenter.PreferredMinimumHeight = (int)(640 * scale);
        }

        // 1280 x 800 effective pixels, but never larger than 90% of the screen's work area.
        var work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var width = Math.Min((int)(1280 * scale), (int)(work.Width * 0.9));
        var height = Math.Min((int)(800 * scale), (int)(work.Height * 0.9));
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(work.X + ((work.Width - width) / 2), work.Y + ((work.Height - height) / 2), width, height));

        _dialogs = new DialogService(() => Content.XamlRoot);
        if (viewModel is not null)
        {
            _dialogs.TargetChanged += (_, _) => viewModel.ShowToast(Reason.Of(ReasonCode.TargetChanged).Text, ToastKind.Warning);
            viewModel.SnapshotApplied += (_, snapshot) => _dialogs.OnSnapshot(snapshot);
            viewModel.FollowUp = (kind, selection) =>
            {
                viewModel.Select(selection);
                return OpenActionSafeAsync(kind);
            };
            AppWindow.Closing += OnClosing;
            _ = LoadAsync(viewModel);
        }
    }

    /// <summary>Developer option <c>--open &lt;action&gt;</c>: opens that dialog after loading (for reviews).</summary>
    public ActionKind? OpenOnStart { get; set; }

    private async Task LoadAsync(MainViewModel viewModel)
    {
        await viewModel.RefreshAsync();

        // Dialogs need the window's content to be loaded (XamlRoot).
        while (Content.XamlRoot is null)
        {
            await Task.Delay(100);
        }

        if (OpenOnStart is { } kind)
        {
            await Task.Delay(500);
            await OpenActionSafeAsync(kind);
        }
    }

    /// <summary>
    /// Reserves exactly the caption buttons' width (AppWindow reports physical pixels) and lets the
    /// pointer reach Refresh and About inside the draggable title bar.
    /// </summary>
    private void UpdateTitleBarLayout()
    {
        if (AppTitleBar.XamlRoot is not { } root)
        {
            return;
        }

        var scale = root.RasterizationScale;
        CaptionButtonsColumn.Width = new GridLength(AppWindow.TitleBar.RightInset / scale);

        var bounds = TitleButtons.TransformToVisual(null).TransformBounds(new Windows.Foundation.Rect(0, 0, TitleButtons.ActualWidth, TitleButtons.ActualHeight));
        var rect = new Windows.Graphics.RectInt32(
            (int)Math.Round(bounds.X * scale),
            (int)Math.Round(bounds.Y * scale),
            (int)Math.Round(bounds.Width * scale),
            (int)Math.Round(bounds.Height * scale));
        Microsoft.UI.Input.InputNonClientPointerSource.GetForWindowId(AppWindow.Id)
            .SetRegionRects(Microsoft.UI.Input.NonClientRegionKind.Passthrough, [rect]);
    }

    private readonly DialogService _dialogs;
    private bool _closeConfirmed;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);

    public MainViewModel Vm { get; }

    public bool HasViewModel => Vm is not null;

    public string TitleSubtitle => Vm is null ? "" : Vm.IsSampleMode ? "Sample disks · nothing is changed" : "Administrator";

    private void OnRefreshClick(object sender, RoutedEventArgs e) => _ = Vm.RefreshAsync();

    private void OnDiskSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DiskList.SelectedItem is DiskView disk && !Equals(disk, Vm.SelectedDisk))
        {
            Vm.SelectedDisk = disk;
        }
    }

    private void OnSegmentSelected(object? sender, SegmentRef? segment) => Vm.SelectSegment(segment);

    private void OnActionsRequested(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (FindFirstButton(ActionGroups) is { } first)
            {
                first.Focus(FocusState.Keyboard);
            }
        });
    }

    private void OnActionClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ActionItem action })
        {
            return;
        }

        if (!action.IsEnabled)
        {
            // Disabled actions stay focusable so their reason can be read; clicking does nothing.
            return;
        }

        _ = OpenActionSafeAsync(action.Kind);
    }

    private void OnInitializeClick(object sender, RoutedEventArgs e) => _ = OpenActionSafeAsync(ActionKind.Initialize);

    private void OnBringOnlineClick(object sender, RoutedEventArgs e) => _ = OpenActionSafeAsync(ActionKind.BringOnline);

    private void OnToastActionClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Toast toast })
        {
            Vm.RemoveToast(toast);
            toast.Action?.Invoke();
        }
    }

    /// <summary>Opens an action without letting an unexpected error vanish silently.</summary>
    private async Task OpenActionSafeAsync(ActionKind kind)
    {
        try
        {
            await OpenActionAsync(kind);
        }
        catch (Exception ex)
        {
            App.Log($"Action {kind} failed: {ex}");
            Vm.ShowToast($"Something went wrong: {ex.Message}", ToastKind.Error);
        }
    }

    /// <summary>
    /// Opens the dialog for an action on the current selection (tasks 12.2-12.4), then runs the
    /// confirmed request and shows the failure dialog if it fails (task 12.1).
    /// </summary>
    private async Task OpenActionAsync(ActionKind kind)
    {
        if (_dialogs.IsOpen)
        {
            return;
        }

        var snapshot = Vm.Snapshot;
        var selection = Vm.CurrentSelection;
        var isDiskAction = kind is ActionKind.Initialize or ActionKind.BringOnline or ActionKind.TakeOffline or ActionKind.ClearDisk;
        ActionTarget? target = isDiskAction
            ? selection.Disk is DiskId diskId ? new DiskTarget(diskId) : null
            : ActionAvailability.ToTarget(selection);

        // The same rules as the buttons: never open a dialog for an action that isn't allowed.
        if (target is null || !ActionAvailability.Evaluate(snapshot, kind, target, Vm.Runner.IsBusy).IsEnabled)
        {
            return;
        }

        var disk = snapshot.FindDisk(selection.Disk!.Value)!;
        var partition = selection.Segment is PartitionRef pr ? snapshot.FindPartition(pr.Id) : null;
        var region = selection.Segment is UnallocatedRef ur ? disk.UnallocatedSegments.FirstOrDefault(s => s.Offset == ur.Offset) : null;
        var provider = Vm.Runner.Provider;

        OperationRequest? request = kind switch
        {
            ActionKind.Initialize => await _dialogs.InitializeAsync(new InitializeDialogModel(snapshot, disk)),
            ActionKind.TakeOffline => await _dialogs.TakeOfflineAsync(new OfflineDialogModel(snapshot, disk)),
            ActionKind.BringOnline => new SetDiskOnlineRequest(new DiskTarget(disk.Id), TargetFingerprint.Of(snapshot, new DiskTarget(disk.Id))!, Online: true),
            ActionKind.ClearDisk => await _dialogs.ClearAsync(new ClearDialogModel(snapshot, disk)),
            ActionKind.CreatePartition when region is not null => await _dialogs.CreateAsync(new CreateDialogModel(snapshot, disk, region), snapshot.Facts),
            ActionKind.DeletePartition when partition is not null => await _dialogs.DeleteAsync(new DeleteDialogModel(snapshot, partition, disk)),
            ActionKind.FormatVolume when partition is not null => await _dialogs.FormatAsync(new FormatDialogModel(snapshot, partition, disk), provider),
            ActionKind.ShrinkVolume when partition is not null => await _dialogs.ShrinkAsync(new ShrinkDialogModel(snapshot, partition), provider),
            ActionKind.ExtendVolume when partition is not null => await _dialogs.ExtendAsync(new ExtendDialogModel(snapshot, partition, disk), provider),
            ActionKind.ChangeDriveLetter when partition is not null => await _dialogs.LetterAsync(new LetterDialogModel(snapshot, partition)),
            ActionKind.ChangeLabel when partition is not null => await _dialogs.LabelAsync(new LabelDialogModel(snapshot, partition)),
            _ => null,
        };

        if (request is null)
        {
            return;
        }

        var before = Vm.Snapshot;
        var result = await Vm.RunOperationAsync(request);
        if (result.Outcome == OperationOutcome.Failed && result.Error is { } error)
        {
            await _dialogs.ShowFailureAsync(OperationTexts.FailedTitle(before, request), error);
        }
    }

    /// <summary>Spec operation-execution "Close during operation".</summary>
    private async void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (!Vm.Runner.IsBusy || _closeConfirmed)
        {
            return;
        }

        args.Cancel = true;
        if (await _dialogs.ConfirmCloseWhileBusyAsync(Vm.BusyText))
        {
            _closeConfirmed = true;
            Close();
        }
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string value } button)
        {
            Ui.CopyWithFeedback(button, value);
        }
    }

    private void OnToastClosed(InfoBar sender, InfoBarClosedEventArgs args)
    {
        if (sender.DataContext is Toast toast)
        {
            Vm.RemoveToast(toast);
        }
    }

    private async void OnAboutClick(object sender, RoutedEventArgs e)
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        var content = new StackPanel { Spacing = 12, MaxWidth = 340 };
        content.Children.Add(new TextBlock { Text = "A free, open-source disk and partition manager for Windows 11.", TextWrapping = TextWrapping.Wrap });
        content.Children.Add(new TextBlock { Text = $"Version {version}", Style = (Style)Application.Current.Resources["CaptionTextStyle"] });
        content.Children.Add(new TextBlock { Text = "MIT License", Style = (Style)Application.Current.Resources["CaptionTextStyle"] });
        var dialog = new ContentDialog
        {
            Title = "WinParted",
            Content = content,
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot,
        };
        await dialog.ShowAsync();
    }

    private void OnRestartAsAdminClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" });
            Close();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // The user declined the prompt; stay on this screen.
        }
    }

    private static Button? FindFirstButton(DependencyObject root)
    {
        for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i);
            if (child is Button b)
            {
                return b;
            }

            if (FindFirstButton(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
