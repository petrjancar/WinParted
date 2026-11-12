using System.Collections.Immutable;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinParted.Core.Formatting;
using WinParted.Core.Model;
using WinParted.Core.Presentation;

namespace WinParted.App.Dialogs;

/// <summary>
/// Size input: number + MB/GB, range caption and inline error (Create, Shrink, Extend). A plain text
/// box so the preview updates while typing (Enter confirms with the value as typed), and a value set by the
/// dialog (e.g. "all free space") keeps its exact byte count even though the text is rounded.
/// </summary>
internal sealed class SizeEditor : UserControl
{
    private readonly TextBox _text = new() { Width = 140 };
    private readonly ComboBox _unit = new() { ItemsSource = new[] { "MB", "GB" }, Width = 90 };
    private readonly TextBlock _range = new() { VerticalAlignment = VerticalAlignment.Center, Style = (Style)Application.Current.Resources["CaptionTextStyle"] };
    private readonly TextBlock _error = new()
    {
        Foreground = Ui.Resource("SystemFillColorCriticalBrush"),
        FontSize = 12,
        TextWrapping = TextWrapping.Wrap,
        Visibility = Visibility.Collapsed,
    };

    private readonly StackPanel _presets = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    private bool _updating;
    private ulong? _exactBytes;
    private string? _exactText;

    public SizeEditor(ulong defaultBytes, SizeUnit unit, string range, string? label = null)
    {
        _unit.SelectedIndex = unit == SizeUnit.GB ? 1 : 0;
        _range.Text = range;
        label ??= Core.Text.Texts.Get("Dlg.Size");
        AutomationHelpers.SetName(_text, label);
        AutomationHelpers.SetName(_unit, "Unit");
        Show(defaultBytes);

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(_text);
        row.Children.Add(_unit);
        row.Children.Add(_presets);
        var root = new StackPanel { Spacing = 6 };
        root.Children.Add(new TextBlock { Text = label });
        root.Children.Add(row);
        root.Children.Add(_range);
        root.Children.Add(_error);
        Content = root;

        _text.TextChanged += (_, _) => RaiseChanged();

        _unit.SelectionChanged += (_, e) =>
        {
            // Keep the same amount when the unit changes.
            if (_updating || e.RemovedItems.Count == 0 || e.RemovedItems[0] is not string old)
            {
                return;
            }

            ulong? bytes = _exactText == _text.Text ? _exactBytes
                : Parse(_text.Text) is double v ? SizeInput.ToBytes(v, old == "GB" ? SizeUnit.GB : SizeUnit.MB)
                : null;
            if (bytes is ulong b)
            {
                Show(b);
            }

            RaiseChanged();
        };
    }

    public event EventHandler? Changed;

    public SizeUnit Unit => _unit.SelectedIndex == 1 ? SizeUnit.GB : SizeUnit.MB;

    /// <summary>The entered size in bytes, or null if the text isn't a number.</summary>
    public ulong? Bytes => _exactBytes is ulong exact && _text.Text == _exactText
        ? exact
        : Parse(_text.Text) is double v ? SizeInput.ToBytes(v, Unit) : null;

    public new bool IsEnabled
    {
        get => _text.IsEnabled;
        set
        {
            _text.IsEnabled = value;
            _unit.IsEnabled = value;
            _presets.IsHitTestVisible = value;
            _presets.Opacity = value ? 1 : 0.5;
        }
    }

    public void SetValue(ulong bytes)
    {
        Show(bytes);
        RaiseChanged();
    }

    public void SetMax(ulong bytes)
    {
        if (Bytes > bytes)
        {
            SetValue(bytes);
        }
    }

    public void SetRange(string text) => _range.Text = text;

    /// <summary>Adds a quick-set button such as "Maximum"; it does nothing while the value is unknown.</summary>
    public Button AddPreset(string label, Func<ulong?> bytes)
    {
        var button = new Button { Content = label };
        button.Click += (_, _) =>
        {
            if (bytes() is ulong b)
            {
                SetValue(b);
            }
        };
        _presets.Children.Add(button);
        return button;
    }

    public void SetError(string? error)
    {
        _error.Text = error ?? "";
        _error.Visibility = error is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Show(ulong bytes)
    {
        _updating = true;
        _exactBytes = bytes;
        _exactText = SizeInput.FromBytes(bytes, Unit).ToString("0.##", System.Globalization.CultureInfo.CurrentCulture);
        _text.Text = _exactText;
        _updating = false;
    }

    private static double? Parse(string text) =>
        double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out var v) ? v : null;

    private void RaiseChanged()
    {
        if (!_updating)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}

/// <summary>File system choice: radio buttons (Create) or a drop-down (Format); disabled options show why.</summary>
internal sealed class FileSystemPicker : UserControl
{
    private readonly bool _asComboBox;
    private readonly List<RadioButton> _radios = [];
    private readonly ComboBox? _combo;
    private readonly StackPanel _reasons = new() { Spacing = 2 };
    private ImmutableArray<Choice<FileSystemKind>> _choices;

    public FileSystemPicker(ImmutableArray<Choice<FileSystemKind>> choices, FileSystemKind selected, bool asComboBox = false)
    {
        _asComboBox = asComboBox;
        _choices = choices;
        Selected = selected;
        var root = new StackPanel { Spacing = 6 };

        if (asComboBox)
        {
            _combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            _combo.SelectionChanged += (_, _) =>
            {
                if (_combo.SelectedItem is Choice<FileSystemKind> c)
                {
                    Selected = c.Value;
                    Changed?.Invoke(this, EventArgs.Empty);
                }
            };
            root.Children.Add(_combo);
        }
        else
        {
            root.Children.Add(new TextBlock { Text = Core.Text.Texts.Get("Dlg.FileSystem") });
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 20 };
            foreach (var c in choices)
            {
                var radio = new RadioButton { Content = c.Text, GroupName = "fs", Tag = c.Value, IsChecked = c.Value == selected };
                radio.Checked += (_, _) =>
                {
                    Selected = (FileSystemKind)radio.Tag;
                    Changed?.Invoke(this, EventArgs.Empty);
                };
                _radios.Add(radio);
                row.Children.Add(radio);
            }

            root.Children.Add(row);
        }

        root.Children.Add(_reasons);
        Content = root;
        Update(choices);
    }

    public event EventHandler? Changed;

    public FileSystemKind Selected { get; private set; }

    public bool IsSelectedEnabled => _choices.FirstOrDefault(c => c.Value == Selected)?.IsEnabled ?? false;

    public void Update(ImmutableArray<Choice<FileSystemKind>> choices)
    {
        _choices = choices;
        _reasons.Children.Clear();
        foreach (var c in choices.Where(c => !c.IsEnabled))
        {
            _reasons.Children.Add(new TextBlock
            {
                Text = $"{c.Text}: {c.DisabledReason}",
                Style = (Style)Application.Current.Resources["CaptionTextStyle"],
            });
        }

        if (_asComboBox && _combo is not null)
        {
            var enabled = choices.Where(c => c.IsEnabled).ToList();
            _combo.ItemsSource = enabled;
            _combo.SelectedItem = enabled.FirstOrDefault(c => c.Value == Selected) ?? enabled.FirstOrDefault();
            return;
        }

        foreach (var radio in _radios)
        {
            var choice = choices.First(c => c.Value == (FileSystemKind)radio.Tag);
            radio.IsEnabled = choice.IsEnabled;
            if (!choice.IsEnabled && radio.IsChecked == true)
            {
                // The chosen file system is no longer possible (e.g. FAT32 after enlarging): fall back to NTFS.
                _radios[0].IsChecked = true;
            }
        }
    }
}

/// <summary>The Create preview: new partition at the start of the region, the rest stays free.</summary>
internal sealed class PreviewBar : UserControl
{
    private readonly Grid _grid = new() { ColumnSpacing = 4, Height = 52 };

    public PreviewBar() => Content = _grid;

    public void Show(string title, string line, ulong size, ulong total, string restTitle)
    {
        _grid.Children.Clear();
        _grid.ColumnDefinitions.Clear();
        var rest = total > size ? total - size : 0;
        _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(size, total / 8), GridUnitType.Star), MinWidth = 140 });
        _grid.Children.Add(Cell(title, line, "ChipAccentBackgroundBrush", "AccentFillColorDefaultBrush", 0));
        if (rest > SizeInput.MiB)
        {
            _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(rest, total / 8), GridUnitType.Star), MinWidth = 140 });
            _grid.Children.Add(Cell(restTitle, SizeFormatter.Format(rest), "LayerFillColorDefaultBrush", "UnallocatedStrokeBrush", 1));
        }
    }

    private static Border Cell(string title, string line, string background, string border, int column)
    {
        var panel = new StackPanel { Spacing = 1 };
        panel.Children.Add(new TextBlock { Text = title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis });
        panel.Children.Add(new TextBlock { Text = line, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis });
        var cell = new Border
        {
            Background = Ui.Resource(background),
            BorderBrush = Ui.Resource(border),
            BorderThickness = new Thickness(column == 0 ? 2 : 1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 6, 10, 6),
            Child = panel,
        };
        Grid.SetColumn(cell, column);
        return cell;
    }
}

internal static class AutomationHelpers
{
    public static void SetName(DependencyObject element, string name) =>
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(element, name);
}
