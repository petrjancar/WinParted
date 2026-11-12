using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.System;
using Windows.UI;
using WinParted.Core.Presentation;
using WinParted.Core.Selections;

namespace WinParted.App.Controls;

/// <summary>
/// The partition layout bar (mvp-features.md 3.4, LayoutBar board): proportional segments with
/// minimum widths, three text modes by width, keyboard navigation with one tab stop.
/// </summary>
public sealed class LayoutBar : UserControl
{
    public static readonly DependencyProperty SegmentsProperty = DependencyProperty.Register(
        nameof(Segments), typeof(object), typeof(LayoutBar), new PropertyMetadata(null, (d, _) => ((LayoutBar)d).Rebuild()));

    public static readonly DependencyProperty SelectedSegmentProperty = DependencyProperty.Register(
        nameof(SelectedSegment), typeof(object), typeof(LayoutBar), new PropertyMetadata(null, (d, _) => ((LayoutBar)d).Rebuild()));

    public static readonly DependencyProperty DiskSizeTextProperty = DependencyProperty.Register(
        nameof(DiskSizeText), typeof(string), typeof(LayoutBar), new PropertyMetadata("", (d, _) => ((LayoutBar)d).Rebuild()));

    private const double Gap = 4;
    private const double SegmentHeight = 96;
    private static readonly ControlTemplate PlainButtonTemplate = (ControlTemplate)XamlReader.Load(
        "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'><ContentPresenter /></ControlTemplate>");

    private readonly StackPanel _bar = new() { Orientation = Orientation.Horizontal, Spacing = Gap };
    private readonly TextBlock _scaleStart = new() { Text = "0" };
    private readonly TextBlock _scaleHint = new() { HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center };
    private readonly TextBlock _scaleEnd = new() { HorizontalAlignment = HorizontalAlignment.Right };
    private readonly List<Button> _buttons = [];
    private double _lastWidth;

    public LayoutBar()
    {
        foreach (var t in new[] { _scaleStart, _scaleHint, _scaleEnd })
        {
            t.Style = (Style)Application.Current.Resources["CaptionTextStyle"];
        }

        var scale = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        scale.Children.Add(_scaleStart);
        scale.Children.Add(_scaleHint);
        scale.Children.Add(_scaleEnd);

        var root = new StackPanel();
        root.Children.Add(_bar);
        root.Children.Add(scale);
        Content = root;

        SizeChanged += (_, e) =>
        {
            if (Math.Abs(e.NewSize.Width - _lastWidth) > 0.5)
            {
                Rebuild();
            }
        };
    }

    public event EventHandler<SegmentRef?>? SegmentSelected;

    /// <summary>Enter or the Menu key on a segment: move focus to the first action.</summary>
    public event EventHandler? ActionsRequested;

    public object? Segments
    {
        get => GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    public object? SelectedSegment
    {
        get => GetValue(SelectedSegmentProperty);
        set => SetValue(SelectedSegmentProperty, value);
    }

    public string DiskSizeText
    {
        get => (string)GetValue(DiskSizeTextProperty);
        set => SetValue(DiskSizeTextProperty, value);
    }

    private IReadOnlyList<SegmentView> Items => Segments as IReadOnlyList<SegmentView> ?? [];

    private void Rebuild()
    {
        _lastWidth = ActualWidth;
        _bar.Children.Clear();
        _buttons.Clear();
        var items = Items;
        if (items.Count == 0 || ActualWidth <= 0)
        {
            return;
        }

        var (widths, notToScale) = SegmentWidths.Compute(
            [.. items.Select(s => s.Size)], [.. items.Select(s => s.Kind == SegmentKind.Unallocated)], ActualWidth, Gap);
        var selected = SelectedSegment as SegmentRef;
        var selectedIndex = items.ToList().FindIndex(s => Equals(s.Ref, selected));

        for (var i = 0; i < items.Count; i++)
        {
            var button = CreateSegment(items[i], Math.Max(0, Math.Floor(widths[i])), i == selectedIndex, i, items.Count);
            button.IsTabStop = i == selectedIndex || (selectedIndex < 0 && i == 0);
            _buttons.Add(button);
            _bar.Children.Add(button);
        }

        AutomationProperties.SetName(_bar, "Partitions. Use the Left and Right arrow keys to move, Enter to go to actions.");
        _scaleHint.Text = notToScale ? Core.Text.Texts.Get("Ui.Bar.NotToScale") : "";
        _scaleEnd.Text = DiskSizeText;
    }

    private Button CreateSegment(SegmentView s, double width, bool isSelected, int index, int count)
    {
        var mode = width < 90 ? 0 : width < 150 ? 1 : 2;
        var fill = new Border
        {
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(s.Kind == SegmentKind.Unallocated ? 0 : 1),
            BorderBrush = Ui.Resource(s.Kind switch
            {
                SegmentKind.Protected or SegmentKind.BitLockerLocked => "SegmentProtectedStrokeBrush",
                SegmentKind.Unknown => "SegmentUnknownStrokeBrush",
                _ => "SegmentStrokeBrush",
            }),
            Background = SegmentBackground(s),
        };

        var text = new StackPanel { Padding = new Thickness(mode == 0 ? 8 : 12, 10, 8, 10), Spacing = 2 };
        var icon = s.IsLocked || s.Kind == SegmentKind.BitLockerLocked ? "" : s.IsEncrypted ? "" : null;
        var title = new TextBlock
        {
            Text = mode == 2 ? s.Title : s.ShortTitle,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            FontSize = 13,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
        };

        if (mode == 0)
        {
            // Narrow: icon above the short title so both fit (LayoutBar board, "Icon" mode).
            if (icon is not null)
            {
                text.Children.Add(new FontIcon { Glyph = icon, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Left });
            }

            text.Children.Add(title);
        }
        else
        {
            // A star column lets the title trim with an ellipsis instead of being clipped.
            // No spacing without an icon, or the empty icon column would push the title right.
            var titleRow = new Grid { ColumnSpacing = icon is null ? 0 : 6 };
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            if (icon is not null)
            {
                titleRow.Children.Add(new FontIcon { Glyph = icon, FontSize = 13 });
            }

            Grid.SetColumn(title, 1);
            titleRow.Children.Add(title);
            text.Children.Add(titleRow);
        }
        if (mode >= 1)
        {
            text.Children.Add(Line(s.Line2));
        }

        if (mode == 2)
        {
            text.Children.Add(Line(s.Line3));
        }

        var content = new Grid { Height = SegmentHeight };
        content.Children.Add(fill);
        if (s.Kind == SegmentKind.Unallocated)
        {
            content.Children.Add(new Rectangle
            {
                RadiusX = 6,
                RadiusY = 6,
                Stroke = Ui.Resource("UnallocatedStrokeBrush"),
                StrokeThickness = 1,
                StrokeDashArray = [4, 3],
            });
        }

        content.Children.Add(text);

        var ring = new Border
        {
            Width = width,
            Padding = new Thickness(2),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(2),
            BorderBrush = isSelected ? Ui.Resource("AccentFillColorDefaultBrush") : new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
            Child = content,
        };

        var button = new Button
        {
            Template = PlainButtonTemplate,
            Content = ring,
            Padding = new Thickness(0),
            UseSystemFocusVisuals = true,
        };
        AutomationProperties.SetName(button, s.AutomationName);
        AutomationProperties.SetPositionInSet(button, index + 1);
        AutomationProperties.SetSizeOfSet(button, count);
        AutomationProperties.SetItemStatus(button, isSelected ? "selected" : "");
        ToolTipService.SetToolTip(button, $"{s.Title}\n{s.Line2} · {s.Line3}" + (s.Role is null ? "" : $"\n{s.Role}"));
        button.Click += (_, _) => SegmentSelected?.Invoke(this, s.Ref);
        button.KeyDown += (_, e) => OnSegmentKey(index, e);
        return button;
    }

    private void OnSegmentKey(int index, KeyRoutedEventArgs e)
    {
        var items = Items;
        var target = e.Key switch
        {
            VirtualKey.Right => Math.Min(index + 1, items.Count - 1),
            VirtualKey.Left => Math.Max(index - 1, 0),
            VirtualKey.Home => 0,
            VirtualKey.End => items.Count - 1,
            _ => -1,
        };

        if (target >= 0)
        {
            e.Handled = true;
            SegmentSelected?.Invoke(this, items[target].Ref);
            DispatcherQueue.TryEnqueue(() =>
            {
                if (target < _buttons.Count)
                {
                    _buttons[target].Focus(FocusState.Keyboard);
                }
            });
        }
        else if (e.Key is VirtualKey.Enter or VirtualKey.Application)
        {
            e.Handled = true;
            SegmentSelected?.Invoke(this, items[index].Ref);
            ActionsRequested?.Invoke(this, EventArgs.Empty);
        }
        else if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            SegmentSelected?.Invoke(this, null);
        }
    }

    private static TextBlock Line(string text) => new()
    {
        Text = text,
        FontSize = 12,
        TextTrimming = TextTrimming.CharacterEllipsis,
        TextWrapping = TextWrapping.NoWrap,
    };

    private static Brush SegmentBackground(SegmentView s)
    {
        switch (s.Kind)
        {
            case SegmentKind.Volume:
                var used = ((SolidColorBrush)Ui.Resource("SegmentUsedBrush")).Color;
                var free = ((SolidColorBrush)Ui.Resource("SegmentFreeBrush")).Color;
                var f = Math.Clamp(s.UsedFraction, 0, 1);
                var brush = new LinearGradientBrush { StartPoint = new(0, 0), EndPoint = new(1, 0) };
                brush.GradientStops.Add(new GradientStop { Color = used, Offset = 0 });
                brush.GradientStops.Add(new GradientStop { Color = used, Offset = f });
                brush.GradientStops.Add(new GradientStop { Color = free, Offset = f });
                brush.GradientStops.Add(new GradientStop { Color = free, Offset = 1 });
                return brush;
            case SegmentKind.Unformatted:
                return Ui.Resource("SegmentFreeBrush");
            case SegmentKind.Unknown:
                return Ui.Resource("SegmentUnknownBrush");
            case SegmentKind.Unallocated:
                return Ui.Resource("LayerFillColorDefaultBrush");
            default:
                return Ui.Resource("SegmentProtectedBrush");
        }
    }
}

/// <summary>The small decorative layout preview in each disk list entry.</summary>
public sealed class MiniBar : UserControl
{
    public static readonly DependencyProperty DiskProperty = DependencyProperty.Register(
        nameof(Disk), typeof(DiskView), typeof(MiniBar), new PropertyMetadata(null, (d, _) => ((MiniBar)d).Rebuild()));

    private readonly Grid _grid = new() { Height = 6, ColumnSpacing = 2 };

    public MiniBar()
    {
        Content = _grid;
        AutomationProperties.SetAccessibilityView(this, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
    }

    public DiskView? Disk
    {
        get => (DiskView?)GetValue(DiskProperty);
        set => SetValue(DiskProperty, value);
    }

    private void Rebuild()
    {
        _grid.Children.Clear();
        _grid.ColumnDefinitions.Clear();
        if (Disk is not { } disk)
        {
            return;
        }

        if (disk.State != DiskState.Normal || disk.Segments.IsEmpty)
        {
            _grid.ColumnDefinitions.Add(new ColumnDefinition());
            _grid.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(3),
                BorderThickness = new Thickness(1),
                BorderBrush = Ui.Resource("UnallocatedStrokeBrush"),
                Background = disk.State == DiskState.Offline ? Ui.Resource("MiniSystemBrush") : null,
            });
            return;
        }

        var total = disk.Segments.Aggregate(0UL, (sum, s) => sum + s.Size);
        for (var i = 0; i < disk.Segments.Length; i++)
        {
            var s = disk.Segments[i];
            var weight = Math.Max(s.Size, total / 50);
            _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(weight, GridUnitType.Star) });
            var cell = new Border
            {
                CornerRadius = new CornerRadius(3),
                Background = s.Kind switch
                {
                    SegmentKind.Volume or SegmentKind.Unformatted => Ui.Resource("MiniVolumeBrush"),
                    SegmentKind.Unknown => Ui.Resource("MiniUnknownBrush"),
                    SegmentKind.Unallocated => null,
                    _ => Ui.Resource("MiniSystemBrush"),
                },
                BorderThickness = new Thickness(s.Kind == SegmentKind.Unallocated ? 1 : 0),
                BorderBrush = Ui.Resource("UnallocatedStrokeBrush"),
            };
            Grid.SetColumn(cell, i);
            _grid.Children.Add(cell);
        }
    }
}
