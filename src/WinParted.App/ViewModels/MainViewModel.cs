using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinParted.Core.Actions;
using WinParted.Core.Model;
using WinParted.Core.Operations;
using WinParted.Core.Presentation;
using WinParted.Core.Selections;

namespace WinParted.App;

public enum ToastKind
{
    Info,
    Success,
    Warning,
    Error,

    /// <summary>Newer release available (update-notice spec): informational, stays until closed, always last.</summary>
    Update,
}

public sealed partial class Toast : ObservableObject
{
    public required string Message { get; init; }

    public ToastKind Kind { get; init; }

    [ObservableProperty]
    public partial bool IsOpen { get; set; } = true;

    /// <summary>Optional follow-up button, e.g. "Initialize now" after Clear disk.</summary>
    public string? ActionText { get; init; }

    public Action? Action { get; init; }

    public bool HasAction => ActionText is not null;
}

/// <summary>
/// State of the main window (mvp-features.md section 3). All text and rules come from
/// <see cref="Presenter"/> and <see cref="ActionAvailability"/>; this class only keeps the current
/// snapshot and selection and raises change notifications.
/// </summary>
public sealed partial class MainViewModel(IStorageProvider provider, bool isSampleMode) : ObservableObject
{
    private static readonly TimeSpan ToastLifetime = TimeSpan.FromSeconds(8);

    private StorageSnapshot _snapshot = StorageSnapshot.Empty;
    private Selection _selection = Selection.None;
    private bool _hasLoaded;

    /// <summary>
    /// One refresh at a time. Overlapping refreshes (a device event during a manual refresh, or two
    /// events a moment apart) would both compare against the same old snapshot and announce the
    /// same connected disk twice.
    /// </summary>
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    public OperationRunner Runner { get; } = new(provider);

    public bool IsSampleMode { get; } = isSampleMode;

    public ObservableCollection<DiskView> Disks { get; } = [];

    public ObservableCollection<Toast> Toasts { get; } = [];

    [ObservableProperty]
    public partial DiskView? SelectedDisk { get; set; }

    [ObservableProperty]
    public partial SegmentRef? SelectedSegment { get; set; }

    [ObservableProperty]
    public partial DetailsView? Details { get; set; }

    [ObservableProperty]
    public partial ActionsView? Actions { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; } = true;

    [ObservableProperty]
    public partial bool IsRefreshing { get; set; }

    [ObservableProperty]
    public partial string? LoadErrorDetails { get; set; }

    public bool HasLoadError => LoadErrorDetails is not null;

    public bool HasNoDisks => !IsLoading && !HasLoadError && Disks.Count == 0;

    public bool ShowContent => !IsLoading && !HasLoadError && Disks.Count > 0;

    public bool IsNormalDisk => SelectedDisk?.State == DiskState.Normal;

    public bool IsNotInitialized => SelectedDisk?.State == DiskState.NotInitialized;

    public bool IsOfflineDisk => SelectedDisk?.State == DiskState.Offline;

    /// <summary>A drive without media, such as an empty card reader slot (change empty-card-reader-slots, D4).</summary>
    public bool IsNoMediaDisk => SelectedDisk?.State == DiskState.NoMedia;

    public StorageSnapshot Snapshot => _snapshot;

    public Selection CurrentSelection => _selection;

    /// <summary>Raised after every new snapshot, so open dialogs can close if their target changed.</summary>
    public event EventHandler<StorageSnapshot>? SnapshotApplied;

    /// <summary>Lets the window offer follow-up dialogs (Initialize now, Create partition) from notifications.</summary>
    public Func<Core.Actions.ActionKind, Selection, Task>? FollowUp { get; set; }

    // ----- Running operations (task 12.1) -----

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string BusyText { get; set; } = "";

    [ObservableProperty]
    public partial double? BusyPercent { get; set; }

    public bool BusyIsIndeterminate => BusyPercent is null;

    public double BusyValue => BusyPercent ?? 0;

    public string BusyHint => BusyPercent is null
        ? $"{Core.Text.Texts.Get("Busy.NoProgress")} {Core.Text.Texts.Get("Busy.Hint")}"
        : $"{BusyPercent:0}% · {Core.Text.Texts.Get("Busy.Hint")}";

    partial void OnBusyPercentChanged(double? value)
    {
        OnPropertyChanged(nameof(BusyIsIndeterminate));
        OnPropertyChanged(nameof(BusyValue));
        OnPropertyChanged(nameof(BusyHint));
    }

    /// <summary>
    /// Runs a confirmed operation (design D7): busy state everywhere, progress, then the new snapshot
    /// and a result notification. Failures are returned for the window to show in the error dialog.
    /// </summary>
    public async Task<OperationResult> RunOperationAsync(OperationRequest request)
    {
        var before = _snapshot;
        BusyText = OperationTexts.Busy(before, request);
        BusyPercent = null;
        var progress = new Progress<OperationProgress>(p => BusyPercent = p.Percent);
        var run = Runner.RunAsync(request, progress);
        IsBusy = true;
        Apply(_snapshot);

        var result = await run;
        IsBusy = false;
        if (result.Snapshot is { } after)
        {
            Apply(after);
            SnapshotApplied?.Invoke(this, after);
        }
        else
        {
            await RefreshAsync();
        }

        RaiseStateChanged();
        switch (result.Outcome)
        {
            case OperationOutcome.Succeeded:
                ShowDone(before, request);
                break;
            case OperationOutcome.TargetChanged:
            case OperationOutcome.Rejected:
                ShowToast(result.Reason?.Text ?? "", ToastKind.Warning);
                break;
        }

        ShowPendingUpdate();
        return result;
    }

    private void ShowDone(StorageSnapshot before, OperationRequest request)
    {
        var message = OperationTexts.Done(before, request);
        if (request is ClearDiskRequest clear && _snapshot.FindDisk(clear.Disk.Disk) is { } cleared)
        {
            // Spec disk-operations "Clear then initialize": Initialize now, or Create partition on
            // a USB stick that Windows left as an empty MBR disk.
            var initialize = cleared.Style == PartitionStyle.Uninitialized;
            var region = cleared.UnallocatedSegments.FirstOrDefault();
            var follow = initialize ? Selection.OfDisk(cleared.Id) : region is null ? null : Selection.OfUnallocated(cleared.Id, region.Offset);
            if (follow is not null && FollowUp is { } followUp)
            {
                ShowToast(message, ToastKind.Success,
                    Core.Text.Texts.Get(initialize ? "Done.InitializeNow" : "Ui.Action.CreatePartition"),
                    () => _ = followUp(initialize ? Core.Actions.ActionKind.Initialize : Core.Actions.ActionKind.CreatePartition, follow));
                return;
            }
        }

        if (request is InitializeDiskRequest init && _snapshot.FindDisk(init.Disk.Disk) is { } initialized
            && initialized.UnallocatedSegments.FirstOrDefault() is { } free)
        {
            // Design "After Initialize": the free space is selected so Create is one step away.
            _selection = Selection.OfUnallocated(initialized.Id, free.Offset);
            UpdateSelectionViews();
        }

        ShowToast(message, ToastKind.Success);
    }

    /// <summary>Developer option (<c>--select disk[:segment]</c>): what to select after the first load.</summary>
    public (int Disk, int? Segment)? InitialSelection { get; set; }

    // ----- Flattened, null-safe values for the window's bindings -----

    public List<Chip> HeaderChips => SelectedDisk?.HeaderChips.ToList() ?? [];

    public List<SegmentView> SegmentList => SelectedDisk?.Segments.ToList() ?? [];

    public string OfflineReasonText => SelectedDisk?.OfflineReason is { } r ? $"Reason from Windows: {r}" : "";

    /// <summary>A partition or unallocated region is selected: the ways back to the disk view are shown (spec disk-inventory).</summary>
    public bool HasSegmentSelected => SelectedSegment is not null;

    public bool IsSelectedDiskReadOnly => SelectedDisk is { } d && _snapshot.FindDisk(d.Id)?.IsReadOnly == true;

    public bool DetailsLocked => Details?.IsLocked ?? false;

    public bool DetailsHasUsage => Details?.HasUsage ?? false;

    public double DetailsUsedPercent => (Details?.UsedFraction ?? 0) * 100;

    public string DetailsHint => Details?.Hint ?? "";

    public List<string> DetailsFlags => Details?.Flags.ToList() ?? [];

    public List<DetailRow> DetailsRows => Details?.Rows.ToList() ?? [];

    public List<ActionGroup> ActionGroupList => Actions?.Groups.ToList() ?? [];

    partial void OnDetailsChanged(DetailsView? value)
    {
        OnPropertyChanged(nameof(DetailsLocked));
        OnPropertyChanged(nameof(DetailsHasUsage));
        OnPropertyChanged(nameof(DetailsUsedPercent));
        OnPropertyChanged(nameof(DetailsHint));
        OnPropertyChanged(nameof(DetailsFlags));
        OnPropertyChanged(nameof(DetailsRows));
    }

    partial void OnActionsChanged(ActionsView? value) => OnPropertyChanged(nameof(ActionGroupList));

    partial void OnSelectedSegmentChanged(SegmentRef? value) => OnPropertyChanged(nameof(HasSegmentSelected));

    /// <summary>Back to the disk's own details and actions (spec disk-inventory "Back to the disk view").</summary>
    public void ShowDisk() => SelectSegment(null);

    /// <summary>Reloads everything (design D3) and keeps the selection when its item still exists.</summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        await _refreshGate.WaitAsync();
        try
        {
            await RefreshCoreAsync();
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private async Task RefreshCoreAsync()
    {
        var before = _snapshot;
        var selectedBefore = _selection.Disk;
        IsRefreshing = _hasLoaded;
        try
        {
            var snapshot = await provider.ReadSnapshotAsync();
            LoadErrorDetails = null;
            Apply(snapshot);
            SnapshotApplied?.Invoke(this, snapshot);
            if (_hasLoaded)
            {
                foreach (var message in ChangeMessages.Between(before, snapshot, selectedBefore, _selection.Disk))
                {
                    ShowToast(message, ToastKind.Info);
                }
            }

            if (!_hasLoaded && InitialSelection is { } initial && snapshot.Disks.FirstOrDefault(d => d.Number == initial.Disk) is { } disk)
            {
                var segments = Presenter.Segments(disk);
                _selection = new Selection(disk.Id, initial.Segment is int i && i < segments.Length ? segments[i].Ref : null);
                SelectedDisk = Disks.First(d => d.Id == disk.Id);
                UpdateSelectionViews();
            }

            _hasLoaded = true;
        }
        catch (Exception ex) when (!_hasLoaded)
        {
            LoadErrorDetails = ex.ToString();
        }
        catch (Exception ex)
        {
            ShowToast($"WinParted couldn't refresh disk information. {ex.Message}", ToastKind.Error);
        }
        finally
        {
            IsLoading = false;
            IsRefreshing = false;
            RaiseStateChanged();
        }
    }

    public void SelectSegment(SegmentRef? segment)
    {
        if (_selection.Disk is not DiskId disk)
        {
            return;
        }

        _selection = new Selection(disk, segment);
        UpdateSelectionViews();
    }

    /// <summary>Selects a disk (and optionally a segment), e.g. for a follow-up dialog.</summary>
    public void Select(Selection selection)
    {
        _selection = selection;
        SelectedDisk = Disks.FirstOrDefault(d => d.Id == selection.Disk);
        _selection = selection;
        UpdateSelectionViews();
    }

    public void ShowToast(string message, ToastKind kind, string? actionText = null, Action? action = null)
    {
        var toast = new Toast { Message = message, Kind = kind, ActionText = actionText, Action = action };
        Toasts.Insert(0, toast);
        if (kind is ToastKind.Info or ToastKind.Success)
        {
            _ = RemoveLaterAsync(toast);
        }
    }

    public void RemoveToast(Toast toast) => Toasts.Remove(toast);

    private Core.Updates.AvailableUpdate? _pendingUpdate;
    private bool _updateShown;

    /// <summary>
    /// Shows the update notice once per start (update-notice spec). While an operation runs it waits
    /// until the operation has ended and its result is shown. It is added at the bottom of the
    /// notifications, so newer results and errors appear above it.
    /// </summary>
    public void ShowUpdate(Core.Updates.AvailableUpdate update)
    {
        if (_updateShown)
        {
            return;
        }

        if (IsBusy)
        {
            App.Log("Update notice held until the running operation ends.");
            _pendingUpdate = update;
            return;
        }

        _updateShown = true;
        App.Log($"Update notice shown for {update.VersionText}.");
        var text = Core.Text.Texts.Get(update.IsPrerelease ? "Update.AvailablePrerelease" : "Update.Available");
        Toasts.Add(new Toast
        {
            Message = string.Format(System.Globalization.CultureInfo.CurrentCulture, text, update.VersionText),
            Kind = ToastKind.Update,
            ActionText = Core.Text.Texts.Get("Update.ViewRelease"),
            Action = () => UpdateService.OpenInBrowser(update.PageUrl),
        });
    }

    private void ShowPendingUpdate()
    {
        if (_pendingUpdate is { } update)
        {
            _pendingUpdate = null;
            ShowUpdate(update);
        }
    }

    partial void OnSelectedDiskChanged(DiskView? value)
    {
        if (value is not null && _selection.Disk != value.Id)
        {
            _selection = Selection.OfDisk(value.Id);
        }

        UpdateSelectionViews();
        RaiseStateChanged();
        OnPropertyChanged(nameof(HeaderChips));
        OnPropertyChanged(nameof(SegmentList));
        OnPropertyChanged(nameof(OfflineReasonText));
    }

    private void Apply(StorageSnapshot snapshot)
    {
        var reconciled = SelectionReconciler.Reconcile(_selection, snapshot);
        _snapshot = snapshot;
        // Nothing selected (first load, or the selected disk was unplugged): show the first disk
        // rather than an empty page.
        _selection = reconciled.Selection.Disk is null && snapshot.Disks.Length > 0
            ? Selection.OfDisk(snapshot.Disks[0].Id)
            : reconciled.Selection;

        Disks.Clear();
        foreach (var disk in snapshot.Disks)
        {
            Disks.Add(Presenter.Disk(disk, Runner.IsBusy && Runner.Current?.Target is { } t && TargetDisk(t) == disk.Id));
        }

        SelectedDisk = Disks.FirstOrDefault(d => d.Id == _selection.Disk);
        UpdateSelectionViews();
    }

    private void UpdateSelectionViews()
    {
        var busy = Runner.IsBusy;
        SelectedSegment = _selection.Segment;
        Details = Presenter.Details(_snapshot, _selection);
        Actions = Presenter.Actions(_snapshot, _selection, busy);
    }

    private void RaiseStateChanged()
    {
        OnPropertyChanged(nameof(HasLoadError));
        OnPropertyChanged(nameof(HasNoDisks));
        OnPropertyChanged(nameof(ShowContent));
        OnPropertyChanged(nameof(IsNormalDisk));
        OnPropertyChanged(nameof(IsNotInitialized));
        OnPropertyChanged(nameof(IsOfflineDisk));
        OnPropertyChanged(nameof(IsNoMediaDisk));
        OnPropertyChanged(nameof(IsSelectedDiskReadOnly));
    }

    private async Task RemoveLaterAsync(Toast toast)
    {
        await Task.Delay(ToastLifetime);
        Toasts.Remove(toast);
    }

    private static DiskId TargetDisk(ActionTarget target) => target switch
    {
        DiskTarget t => t.Disk,
        PartitionTarget t => t.Partition.Disk,
        UnallocatedTarget t => t.Disk,
        _ => default,
    };
}
