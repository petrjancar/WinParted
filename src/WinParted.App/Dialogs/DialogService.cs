using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using WinParted.Core.Formatting;
using WinParted.Core.Model;
using WinParted.Core.Operations;
using WinParted.Core.Presentation;
using WinParted.Core.Text;

namespace WinParted.App.Dialogs;

/// <summary>
/// Builds the operation dialogs (mvp-features.md section 6, tasks 12.2-12.4) from the Core dialog
/// models. Each Show method returns the confirmed request, or null if the user cancelled or the
/// dialog closed itself because its target changed (task 12.5).
/// </summary>
public sealed class DialogService(Func<XamlRoot> xamlRoot)
{
    private ContentDialog? _open;
    private DialogModel? _openModel;

    /// <summary>Raised when an open dialog closed itself because its disk changed.</summary>
    public event EventHandler? TargetChanged;

    public bool IsOpen => _open is not null;

    /// <summary>Closes the open dialog if the snapshot no longer matches its target (spec device-change-detection).</summary>
    public void OnSnapshot(StorageSnapshot snapshot)
    {
        if (_open is { } dialog && _openModel is { } model && model.IsStale(snapshot))
        {
            _openModel = null;
            dialog.Hide();
            TargetChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    // ----- Disk dialogs ---------------------------------------------------------------------

    public async Task<OperationRequest?> InitializeAsync(InitializeDialogModel model)
    {
        var style = PartitionStyle.Gpt;
        var result = Caption(InitializeDialogModel.ResultHint(style));
        var warning = Bar(model.MbrWarning ?? "", InfoBarSeverity.Warning);
        warning.Visibility = Visibility.Collapsed;

        var gpt = Option(Texts.Get("Dlg.Initialize.Gpt"), Texts.Get("Dlg.Initialize.GptHint"), "style", isChecked: true);
        var mbr = Option(Texts.Get("Dlg.Initialize.Mbr"), Texts.Get("Dlg.Initialize.MbrHint"), "style", isChecked: false);
        void Update()
        {
            style = mbr.IsChecked == true ? PartitionStyle.Mbr : PartitionStyle.Gpt;
            result.Text = InitializeDialogModel.ResultHint(style);
            warning.Visibility = style == PartitionStyle.Mbr && model.MbrWarning is not null ? Visibility.Visible : Visibility.Collapsed;
        }

        gpt.Checked += (_, _) => Update();
        mbr.Checked += (_, _) => Update();

        var content = Panel(Identity(model.IdentityText), Strong(Texts.Get("Dlg.Initialize.Style")), gpt, mbr, warning, result);
        return await ShowAsync(model, content) ? model.Request(style) : null;
    }

    public async Task<OperationRequest?> TakeOfflineAsync(OfflineDialogModel model)
    {
        var content = Panel(Identity(model.IdentityText), Body(model.Body));
        return await ShowAsync(model, content) ? model.Request() : null;
    }

    public async Task<OperationRequest?> ClearAsync(ClearDialogModel model)
    {
        var input = new TextBox { Width = 96, HorizontalAlignment = HorizontalAlignment.Left };
        var content = Panel(
            Identity(model.IdentityText),
            SegmentStrip(model.Segments),
            Bar(model.Warning, InfoBarSeverity.Error),
            Field(model.ConfirmLabel, input));

        var dialog = Dialog(model, content);
        dialog.IsPrimaryButtonEnabled = false;
        input.TextChanged += (_, _) => dialog.IsPrimaryButtonEnabled = model.IsConfirmed(input.Text);
        return await RunAsync(dialog, model) ? model.Request() : null;
    }

    // ----- Partition dialogs ------------------------------------------------------------------

    public async Task<OperationRequest?> CreateAsync(CreateDialogModel model, SystemFacts facts)
    {
        var size = new SizeEditor(model.MaxSize, model.DefaultUnit, model.RangeText);
        size.AddPreset(Texts.Get("Dlg.UseMaximum"), () => model.MaxSize);
        var formatOn = Radio(Texts.Get("Dlg.Create.Format"), "fmt", isChecked: true);
        var formatOff = Radio(Texts.Get("Dlg.Create.DontFormat"), "fmt", isChecked: false);
        // One hint line for both choices keeps the dialog the same size when switching.
        var dontFormatHint = Caption(Texts.Get("Dlg.Create.FormatHint"));
        var fs = new FileSystemPicker(model.FileSystems(model.MaxSize), FileSystemKind.Ntfs);
        var label = new TextBox { PlaceholderText = Texts.Get("Dlg.LabelPlaceholder") };
        var labelError = Error();
        var letters = new ComboBox { ItemsSource = model.Letters(facts), SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var quick = new CheckBox { Content = Texts.Get("Dlg.QuickFormat"), IsChecked = true };
        var quickReason = Caption("");
        var fullHint = Caption(Texts.Get("Dlg.FullFormatHint"));
        var preview = new PreviewBar();

        var formatFields = Panel(
            fs,
            TwoColumns(Field(Texts.Get("Dlg.Label"), label, labelError), Field(Texts.Get("Dlg.DriveLetter"), letters)),
            quick,
            quickReason,
            fullHint);

        var content = Panel(
            Header(model.Title, model.Subtitle),
            preview,
            size,
            Row(formatOn, formatOff),
            dontFormatHint,
            formatFields);
        var dialog = Dialog(model, content, showTitle: false);

        void Validate()
        {
            var format = formatOn.IsChecked == true;
            formatFields.Opacity = format ? 1 : 0.5;
            formatFields.IsHitTestVisible = format;
            dontFormatHint.Text = Texts.Get(format ? "Dlg.Create.FormatHint" : "Dlg.Create.DontFormatHint");

            var sizeError = model.SizeError(size.Bytes);
            size.SetError(sizeError);
            var bytes = size.Bytes ?? 0;
            fs.Update(model.FileSystems(Math.Min(bytes, model.MaxSize)));
            var lblError = format ? FormatChoices.LabelError(fs.Selected, label.Text) : null;
            labelError.Text = lblError ?? "";
            labelError.Visibility = lblError is null ? Visibility.Collapsed : Visibility.Visible;
            Quick(quick, quickReason, fullHint, fs.Selected, model.Bus);
            preview.Show(Texts.Get("Dlg.Create.NewPartition"),
                format ? $"{Texts.FileSystem(fs.Selected)} · {SizeFormatter.Format(Math.Min(bytes, model.MaxSize))}" : $"{Texts.Get("Ui.Segment.NoFileSystem")} · {SizeFormatter.Format(Math.Min(bytes, model.MaxSize))}",
                Math.Min(bytes, model.MaxSize), model.MaxSize, Texts.Get("Dlg.Create.StaysFree"));
            dialog.IsPrimaryButtonEnabled = sizeError is null && lblError is null && (!format || fs.IsSelectedEnabled);
        }

        size.Changed += (_, _) => Validate();
        formatOn.Checked += (_, _) => Validate();
        formatOff.Checked += (_, _) => Validate();
        fs.Changed += (_, _) => Validate();
        label.TextChanged += (_, _) => Validate();
        Validate();

        if (!await RunAsync(dialog, model))
        {
            return null;
        }

        var options = formatOn.IsChecked == true
            ? new FormatOptions(fs.Selected, string.IsNullOrEmpty(label.Text) ? null : label.Text, quick.IsChecked == true)
            : null;
        return model.Request(size.Bytes ?? 0, options, (letters.SelectedItem as Choice<char?>)?.Value);
    }

    public async Task<OperationRequest?> DeleteAsync(DeleteDialogModel model)
    {
        var content = Panel(SegmentStrip([model.Segment]), Body(model.Body));
        if (model.Note is { } note)
        {
            content.Children.Add(model.Partition.Content == PartitionContent.Foreign ? Caption(note) : Body(note));
        }

        return await ShowAsync(model, content) ? model.Request() : null;
    }

    public async Task<OperationRequest?> FormatAsync(FormatDialogModel model, IStorageProvider provider)
    {
        var fs = new FileSystemPicker(model.FileSystems, model.DefaultFileSystem, asComboBox: true);
        var label = new TextBox { Text = model.DefaultLabel };
        var labelError = Error();
        var units = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        var quick = new CheckBox { Content = Texts.Get("Dlg.QuickFormat"), IsChecked = true };
        var quickReason = Caption("");
        var fullHint = Caption(Texts.Get("Dlg.FullFormatHint"));
        var content = Panel(
            Bar(model.Body + " " + model.ProgramsWarning, InfoBarSeverity.Error),
            Caption(model.IdentityText),
            TwoColumns(Field(Texts.Get("Dlg.FileSystem"), fs), Field(Texts.Get("Dlg.Label"), label, labelError)),
            Field(Texts.Get("Dlg.AllocationUnit"), units),
            quick,
            quickReason,
            fullHint);
        if (model.KeepsLetterText is { } keeps)
        {
            content.Children.Add(Caption(keeps));
        }

        var dialog = Dialog(model, content);

        async Task LoadUnitsAsync()
        {
            var selectedFs = fs.Selected;
            var items = new List<Choice<uint?>> { new(Texts.Get("Dlg.AllocationDefault"), null) };
            try
            {
                var sizes = await provider.GetSupportedClusterSizesAsync(model.Partition.Id, selectedFs);
                items.AddRange(sizes.Select(s => new Choice<uint?>(s >= 1024 ? $"{s / 1024} KB" : $"{s} bytes", s)));
            }
            catch (Exception ex) when (ex is StorageOperationException or InvalidOperationException)
            {
            }

            if (selectedFs == fs.Selected)
            {
                units.ItemsSource = items;
                units.SelectedIndex = 0;
            }
        }

        void Validate()
        {
            var error = FormatChoices.LabelError(fs.Selected, label.Text);
            labelError.Text = error ?? "";
            labelError.Visibility = error is null ? Visibility.Collapsed : Visibility.Visible;
            Quick(quick, quickReason, fullHint, fs.Selected, model.Bus);
            dialog.IsPrimaryButtonEnabled = error is null && fs.IsSelectedEnabled;
        }

        fs.Changed += (_, _) =>
        {
            Validate();
            _ = LoadUnitsAsync();
        };
        label.TextChanged += (_, _) => Validate();
        quick.Checked += (_, _) => Validate();
        quick.Unchecked += (_, _) => Validate();
        Validate();
        _ = LoadUnitsAsync();

        if (!await RunAsync(dialog, model))
        {
            return null;
        }

        return model.Request(new FormatOptions(
            fs.Selected,
            string.IsNullOrEmpty(label.Text) ? null : label.Text,
            quick.IsChecked == true,
            (units.SelectedItem as Choice<uint?>)?.Value));
    }

    public async Task<OperationRequest?> ShrinkAsync(ShrinkDialogModel model, IStorageProvider provider)
    {
        var smallest = Value(Texts.Get("Dlg.Shrink.Calculating"));
        var calculating = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        calculating.Children.Add(new ProgressRing { IsActive = true, Width = 20, Height = 20 });
        calculating.Children.Add(Panel(Strong(Texts.Get("Dlg.Shrink.Calculating")), Caption(Texts.Get("Dlg.Shrink.CalculatingHint"))));
        var size = new SizeEditor(model.MaxSize, SizeInput.BestUnit(model.Partition.Size), "", Texts.Get("Dlg.NewSize")) { IsEnabled = false };
        ulong? minimum = null;
        size.AddPreset(Texts.Get("Dlg.UseSmallest"), () => minimum is ulong m ? ShrinkDialogModel.AlignUp(m) : null);
        var explanation = Caption("");
        var result = Caption(model.ResultText);
        var content = Panel(
            Facts((Texts.Get("Dlg.Shrink.Current"), model.CurrentText), (Texts.Get("Dlg.Shrink.Used"), model.UsedText), (Texts.Get("Dlg.Shrink.Smallest"), smallest)),
            calculating,
            size,
            result,
            explanation,
            Caption(Texts.Get("Dlg.Shrink.Slow")));
        var dialog = Dialog(model, content);
        dialog.IsPrimaryButtonEnabled = false;

        void Validate()
        {
            if (minimum is not ulong min)
            {
                return;
            }

            var error = model.SizeError(size.Bytes, min);
            size.SetError(error);
            if (error is null)
            {
                size.SetRange($"{SizeInput.Precise(ShrinkDialogModel.AlignUp(min))} – {SizeInput.Precise(model.MaxSize)} · {model.FreesText(size.Bytes ?? 0)}");
            }

            dialog.IsPrimaryButtonEnabled = error is null;
        }

        size.Changed += (_, _) => Validate();
        var cts = new CancellationTokenSource();
        dialog.Closed += (_, _) => cts.Cancel();
        _ = LoadMinimumAsync();

        async Task LoadMinimumAsync()
        {
            try
            {
                var range = await provider.GetSupportedSizeAsync(model.Partition.Id, cts.Token);
                minimum = range.Minimum;
                smallest.Text = SizeFormatter.Format(range.Minimum);
                calculating.Visibility = Visibility.Collapsed;
                explanation.Text = model.MinimumExplanation(range.Minimum) ?? "";
                size.IsEnabled = true;
                size.SetValue(Math.Max(ShrinkDialogModel.AlignUp(range.Minimum), model.Partition.Size / 2 / SizeInput.MiB * SizeInput.MiB));
                Validate();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex) when (ex is StorageOperationException or InvalidOperationException)
            {
                calculating.Visibility = Visibility.Collapsed;
                smallest.Text = "–";
                explanation.Text = Texts.Get("Dlg.Shrink.Failed");
            }
        }

        return await RunAsync(dialog, model) ? model.Request(size.Bytes ?? 0) : null;
    }

    public async Task<OperationRequest?> ExtendAsync(ExtendDialogModel model, IStorageProvider provider)
    {
        var max = model.LayoutMax;
        var size = new SizeEditor(max, SizeInput.BestUnit(max), "", Texts.Get("Dlg.NewSize"));
        size.AddPreset(Texts.Get("Dlg.UseMaximum"), () => max);
        size.SetValue(max);
        var result = Caption(model.ResultText(max));
        var content = Panel(
            Facts((Texts.Get("Dlg.Shrink.Current"), model.CurrentText), (Texts.Get("Dlg.Extend.FreeAfter"), model.FreeAfterText)),
            size,
            result);
        var dialog = Dialog(model, content);

        void Validate()
        {
            var error = model.SizeError(size.Bytes, max);
            size.SetError(error);
            size.SetRange(Texts.Get("Dlg.Range") is { } r ? string.Format(System.Globalization.CultureInfo.CurrentCulture, r, SizeInput.Precise(model.MinSize), SizeInput.Precise(max)) : "");
            result.Text = model.ResultText(Math.Min(size.Bytes ?? 0, max));
            dialog.IsPrimaryButtonEnabled = error is null;
        }

        size.Changed += (_, _) => Validate();
        Validate();

        // Windows may allow slightly less than the layout suggests; use its maximum when it answers.
        _ = Task.Run(async () =>
        {
            try
            {
                var range = await provider.GetSupportedSizeAsync(model.Partition.Id);
                if (range.Maximum > model.MinSize && range.Maximum < max)
                {
                    dialog.DispatcherQueue.TryEnqueue(() =>
                    {
                        max = range.Maximum / SizeInput.MiB * SizeInput.MiB;
                        size.SetMax(max);
                        Validate();
                    });
                }
            }
            catch (Exception ex) when (ex is StorageOperationException or InvalidOperationException)
            {
            }
        });

        return await RunAsync(dialog, model) ? model.Request(Math.Min(size.Bytes ?? 0, max)) : null;
    }

    public async Task<OperationRequest?> LetterAsync(LetterDialogModel model)
    {
        var letters = new ComboBox { ItemsSource = model.Letters, SelectedItem = model.DefaultChoice, MinWidth = 200 };
        var warning = Bar("", InfoBarSeverity.Informational);
        var content = Panel(Field(Texts.Get("Dlg.DriveLetter"), letters), warning);
        var dialog = Dialog(model, content);

        void Update()
        {
            var letter = (letters.SelectedItem as Choice<char?>)?.Value;
            var text = model.Warning(letter);
            warning.Message = text ?? "";
            warning.Severity = letter is null ? InfoBarSeverity.Warning : InfoBarSeverity.Informational;
            warning.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
            dialog.PrimaryButtonText = model.PrimaryTextFor(letter);
            dialog.IsPrimaryButtonEnabled = model.CanApply(letter);
        }

        letters.SelectionChanged += (_, _) => Update();
        Update();
        return await RunAsync(dialog, model) ? model.Request((letters.SelectedItem as Choice<char?>)?.Value) : null;
    }

    public async Task<OperationRequest?> LabelAsync(LabelDialogModel model)
    {
        var label = new TextBox { Text = model.DefaultLabel };
        var error = Error();
        var count = Caption(model.CountText(model.DefaultLabel));
        var content = Panel(Field(Texts.Get("Dlg.Label"), label, error, count));
        var dialog = Dialog(model, content);

        void Validate()
        {
            var e = model.Error(label.Text);
            error.Text = e ?? "";
            error.Visibility = e is null ? Visibility.Collapsed : Visibility.Visible;
            count.Text = model.CountText(label.Text);
            dialog.IsPrimaryButtonEnabled = model.CanApply(label.Text);
        }

        label.TextChanged += (_, _) => Validate();
        Validate();
        dialog.Opened += (_, _) =>
        {
            label.Focus(FocusState.Programmatic);
            label.SelectionStart = label.Text.Length;
        };
        return await RunAsync(dialog, model) ? model.Request(label.Text) : null;
    }

    // ----- Errors and closing -------------------------------------------------------------------

    /// <summary>The failure dialog (Operations board): what happened, was anything changed, what to do, details.</summary>
    public async Task ShowFailureAsync(string title, StorageError error)
    {
        var details = $"{error.Details}{(error.Code is uint c ? $"{Environment.NewLine}Code: {c}" : "")}";
        var copy = new Button { Content = Texts.Get("Fail.Copy") };
        copy.Click += (_, _) => Ui.CopyWithFeedback(copy, $"{title}{Environment.NewLine}{error.Message}{Environment.NewLine}{details}");

        var detailsText = new TextBlock
        {
            Text = details,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
        };
        var expander = new Expander
        {
            Header = Texts.Get("Fail.Details"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = Panel(detailsText, copy),
        };

        var content = Panel(
            Facts((Texts.Get("Fail.WhatHappened"), Value(error.Message)),
                  (Texts.Get("Fail.Changed"), Value(Texts.Get(error.MayHaveChanged ? "Fail.ChangedMaybe" : "Fail.ChangedNo"))),
                  (Texts.Get("Fail.WhatToDo"), Value(Texts.Get("Fail.WhatToDoText")))),
            expander);

        var dialog = new ContentDialog
        {
            Title = title,
            Content = content,
            CloseButtonText = Texts.Get("Dlg.Close"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = xamlRoot(),
        };
        await dialog.ShowAsync();
    }

    /// <summary>Spec operation-execution "Close during operation". Returns true to close anyway.</summary>
    public async Task<bool> ConfirmCloseWhileBusyAsync(string busyText)
    {
        var dialog = new ContentDialog
        {
            Title = Texts.Get("Close.Title"),
            Content = Body(string.Format(System.Globalization.CultureInfo.CurrentCulture, Texts.Get("Close.Body"), busyText)),
            PrimaryButtonText = Texts.Get("Close.Keep"),
            SecondaryButtonText = Texts.Get("Close.Anyway"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot(),
        };
        return await dialog.ShowAsync() == ContentDialogResult.Secondary;
    }

    // ----- Plumbing -----------------------------------------------------------------------------

    private ContentDialog Dialog(DialogModel model, UIElement content, bool showTitle = true)
    {
        var dialog = new ContentDialog
        {
            Title = showTitle ? model.Title : null,
            Content = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            PrimaryButtonText = model.PrimaryText,
            CloseButtonText = Texts.Get("Dlg.Cancel"),
            PrimaryButtonStyle = (Style)Application.Current.Resources[model.IsDestructive ? "DangerButtonStyle" : "AccentButtonStyle"],
            XamlRoot = xamlRoot(),
        };

        HighlightDefaultButton(dialog, model);
        return dialog;
    }

    /// <summary>
    /// The one keyboard rule for every dialog: Enter presses the highlighted (default) button and Escape
    /// cancels. The confirm button is the default while the input is valid; while it's disabled no button is
    /// highlighted, so Enter does nothing. Delete has nothing to type, so Cancel is its default.
    /// Enter in a text field confirms with the value as typed, because sizes and labels are parsed live.
    /// </summary>
    private static void HighlightDefaultButton(ContentDialog dialog, DialogModel model)
    {
        void Update() => dialog.DefaultButton =
            !model.EnterConfirms ? ContentDialogButton.Close
            : dialog.IsPrimaryButtonEnabled ? ContentDialogButton.Primary
            : ContentDialogButton.None;

        Update();
        dialog.RegisterPropertyChangedCallback(ContentDialog.IsPrimaryButtonEnabledProperty, (_, _) => Update());
    }

    private async Task<bool> ShowAsync(DialogModel model, UIElement content) => await RunAsync(Dialog(model, content), model);

    private async Task<bool> RunAsync(ContentDialog dialog, DialogModel model)
    {
        _open = dialog;
        _openModel = model;
        App.Log($"Dialog: {model.Title}");
        try
        {
            var result = await dialog.ShowAsync();

            // A dialog that closed itself because its target changed never confirms (task 12.5).
            return result == ContentDialogResult.Primary && _openModel is not null;
        }
        finally
        {
            _open = null;
            _openModel = null;
        }
    }

    private static void Quick(CheckBox quick, TextBlock reason, TextBlock fullHint, FileSystemKind fs, BusType bus)
    {
        var why = FormatChoices.FullFormatReason(fs, bus);
        if (why is not null)
        {
            quick.IsChecked = true;
        }

        quick.IsEnabled = why is null;
        reason.Text = why ?? "";
        reason.Visibility = why is null ? Visibility.Collapsed : Visibility.Visible;
        fullHint.Visibility = quick.IsChecked == true ? Visibility.Collapsed : Visibility.Visible;
    }

    internal static StackPanel Panel(params UIElement[] children)
    {
        var panel = new StackPanel { Spacing = 12, Width = 480 };
        foreach (var c in children)
        {
            panel.Children.Add(c);
        }

        return panel;
    }

    private static Grid TwoColumns(UIElement left, UIElement right)
    {
        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(left);
        Grid.SetColumn((FrameworkElement)right, 1);
        grid.Children.Add(right);
        return grid;
    }

    private static StackPanel Row(params UIElement[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 20 };
        foreach (var c in children)
        {
            row.Children.Add(c);
        }

        return row;
    }

    private static StackPanel Field(string label, params UIElement[] controls)
    {
        var field = new StackPanel { Spacing = 6 };
        field.Children.Add(new TextBlock { Text = label });
        foreach (var c in controls)
        {
            field.Children.Add(c);
        }

        return field;
    }

    private static StackPanel Header(string title, string subtitle) =>
        Panel(new TextBlock { Text = title, Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"] }, Caption(subtitle));

    private static Grid Facts(params (string Label, object Value)[] rows)
    {
        var grid = new Grid { ColumnSpacing = 16, RowSpacing = 6 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (var i = 0; i < rows.Length; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = new TextBlock { Text = rows[i].Label, Foreground = Ui.Resource("TextFillColorSecondaryBrush") };
            Grid.SetRow(label, i);
            grid.Children.Add(label);
            var value = rows[i].Value as FrameworkElement ?? Value(rows[i].Value.ToString() ?? "");
            Grid.SetRow(value, i);
            Grid.SetColumn(value, 1);
            grid.Children.Add(value);
        }

        return grid;
    }

    private static TextBlock Value(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap };

    private static TextBlock Body(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap };

    private static TextBlock Strong(string text) => new() { Text = text, FontWeight = FontWeights.SemiBold };

    private static TextBlock Caption(string text) => new() { Text = text, Style = (Style)Application.Current.Resources["CaptionTextStyle"] };

    private static TextBlock Error() => new()
    {
        Foreground = Ui.Resource("SystemFillColorCriticalBrush"),
        FontSize = 12,
        TextWrapping = TextWrapping.Wrap,
        Visibility = Visibility.Collapsed,
    };

    private static Border Identity(string text) => new()
    {
        Background = Ui.Resource("SubtleFillColorSecondaryBrush"),
        CornerRadius = new CornerRadius(6),
        Padding = new Thickness(12, 10, 12, 10),
        Child = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap },
    };

    private static InfoBar Bar(string message, InfoBarSeverity severity) => new()
    {
        IsOpen = true,
        IsClosable = false,
        Severity = severity,
        Message = message,
    };

    private static RadioButton Radio(string text, string group, bool isChecked) => new() { Content = text, GroupName = group, IsChecked = isChecked };

    private static RadioButton Option(string title, string hint, string group, bool isChecked) => new()
    {
        GroupName = group,
        IsChecked = isChecked,
        Content = Panel(Strong(title), Caption(hint)),
    };

    /// <summary>A small labelled partition strip, so the user recognises what will be lost.</summary>
    private static Grid SegmentStrip(IReadOnlyList<SegmentView> segments)
    {
        var grid = new Grid { ColumnSpacing = 4 };
        for (var i = 0; i < segments.Count; i++)
        {
            var s = segments[i];
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(s.Size, 1), GridUnitType.Star) });
            var cell = new Border
            {
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                BorderBrush = Ui.Resource(s.Kind == SegmentKind.Unallocated ? "UnallocatedStrokeBrush" : "SegmentStrokeBrush"),
                Background = Ui.Resource(s.Kind switch
                {
                    SegmentKind.Volume => "SegmentUsedBrush",
                    SegmentKind.Unknown => "SegmentUnknownBrush",
                    SegmentKind.Unallocated => "LayerFillColorDefaultBrush",
                    _ => "SegmentProtectedBrush",
                }),
                Padding = new Thickness(10, 6, 10, 6),
                MinHeight = 52,
                Child = Panel2(new TextBlock { Text = s.Title, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis },
                               new TextBlock { Text = $"{s.Line2} · {s.Line3}", FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis }),
            };
            Grid.SetColumn(cell, i);
            grid.Children.Add(cell);
        }

        return grid;
    }

    private static StackPanel Panel2(params UIElement[] children)
    {
        var p = new StackPanel { Spacing = 1 };
        foreach (var c in children)
        {
            p.Children.Add(c);
        }

        return p;
    }
}
