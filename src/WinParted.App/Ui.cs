using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WinParted.Core.Presentation;

namespace WinParted.App;

/// <summary>Small helpers called from x:Bind function bindings.</summary>
public static class Ui
{
    public static Brush Resource(string key) => (Brush)Application.Current.Resources[key];

    /// <summary>
    /// Copies text and confirms it in place: the button shows "Copied" (a check mark for icon buttons) for two
    /// seconds and screen readers announce it. No notification, which would be too loud for a copy.
    /// </summary>
    public static void CopyWithFeedback(Button button, string text)
    {
        var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
        package.SetText(text);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);

        var copied = WinParted.Core.Text.Texts.Get("Ui.Copied");
        if (button.Tag is CopyState)
        {
            return; // already showing "Copied"
        }

        var state = new CopyState(button.Content, Microsoft.UI.Xaml.Controls.ToolTipService.GetToolTip(button), button.Tag);
        button.Tag = state;
        button.Content = button.Content is FontIcon icon
            ? new FontIcon { Glyph = "", FontSize = icon.FontSize }
            : copied;
        Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(button, copied);

        var peer = Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.FromElement(button)
            ?? Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.CreatePeerForElement(button);
        peer?.RaiseNotificationEvent(
            Microsoft.UI.Xaml.Automation.Peers.AutomationNotificationKind.ActionCompleted,
            Microsoft.UI.Xaml.Automation.Peers.AutomationNotificationProcessing.MostRecent,
            copied,
            "WinPartedCopied");

        var timer = button.DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(2);
        timer.IsRepeating = false;
        timer.Tick += (_, _) =>
        {
            button.Content = state.Content;
            Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(button, state.ToolTip);
            button.Tag = state.Tag;
        };
        timer.Start();
    }

    private sealed record CopyState(object Content, object ToolTip, object Tag);

    public static Brush ChipForeground(ChipTone tone) => tone switch
    {
        ChipTone.Success => Resource("SystemFillColorSuccessBrush"),
        ChipTone.Warning => Resource("SystemFillColorCautionBrush"),
        ChipTone.Critical => Resource("SystemFillColorCriticalBrush"),
        ChipTone.Accent => Resource("AccentTextFillColorPrimaryBrush"),
        _ => Resource("TextFillColorSecondaryBrush"),
    };

    public static Brush ChipBackground(ChipTone tone) => tone switch
    {
        ChipTone.Warning => Resource("ChipWarningBackgroundBrush"),
        ChipTone.Critical => Resource("ChipCriticalBackgroundBrush"),
        ChipTone.Accent => Resource("ChipAccentBackgroundBrush"),
        _ => new SolidColorBrush(Microsoft.UI.Colors.Transparent),
    };

    public static string Glyph(ChipIcon icon) => icon switch
    {
        ChipIcon.Lock => "",
        ChipIcon.Power => "",
        ChipIcon.NotInitialized => "",
        ChipIcon.Warning => "",
        ChipIcon.Error => "",
        ChipIcon.Check => "",
        ChipIcon.Dot => "",
        ChipIcon.Busy => "",
        ChipIcon.Usb => "",
        ChipIcon.Sd => "",
        ChipIcon.Virtual => "",
        ChipIcon.PartitionStyle => "",
        _ => "",
    };

    public static Visibility Show(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility ShowIf(object? value) => value is null || value is string { Length: 0 } ? Visibility.Collapsed : Visibility.Visible;

    public static Visibility Hide(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    public static object Chips(System.Collections.Immutable.ImmutableArray<Chip> items) => items.IsDefault ? new List<Chip>() : items.ToList();

    public static object Items(System.Collections.Immutable.ImmutableArray<ActionItem> items) => items.IsDefault ? new List<ActionItem>() : items.ToList();

    /// <summary>
    /// Disabled actions stay focusable (so the reason can be read) but use the disabled look and
    /// ignore the pointer (IsHitTestVisible=false), so hovering doesn't make them look clickable.
    /// </summary>
    public static Brush ActionBackground(bool enabled) =>
        enabled ? Resource("ButtonBackground") : new SolidColorBrush(Microsoft.UI.Colors.Transparent);

    public static Brush ActionForeground(bool enabled) =>
        Resource(enabled ? "TextFillColorPrimaryBrush" : "TextFillColorDisabledBrush");

    public static InfoBarSeverity Severity(ToastKind kind) => kind switch
    {
        ToastKind.Success => InfoBarSeverity.Success,
        ToastKind.Warning => InfoBarSeverity.Warning,
        ToastKind.Error => InfoBarSeverity.Error,
        _ => InfoBarSeverity.Informational,
    };

    public static string ActionGlyph(Core.Actions.ActionKind kind) => kind switch
    {
        Core.Actions.ActionKind.Initialize => "",
        Core.Actions.ActionKind.BringOnline or Core.Actions.ActionKind.TakeOffline => "",
        Core.Actions.ActionKind.ClearDisk => "",
        Core.Actions.ActionKind.CreatePartition => "",
        Core.Actions.ActionKind.FormatVolume => "",
        Core.Actions.ActionKind.DeletePartition => "",
        Core.Actions.ActionKind.ShrinkVolume => "",
        Core.Actions.ActionKind.ExtendVolume => "",
        Core.Actions.ActionKind.ChangeDriveLetter => "",
        Core.Actions.ActionKind.ChangeLabel => "",
        _ => "",
    };
}
