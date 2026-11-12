using WinParted.Core.Actions;
using WinParted.Core.Model;

namespace WinParted.Core.Operations;

public enum OperationOutcome
{
    Succeeded,

    /// <summary>Not executed: the action isn't allowed (busy, protected, precondition failed).</summary>
    Rejected,

    /// <summary>Not executed: the target disappeared or changed since the user confirmed.</summary>
    TargetChanged,

    /// <summary>Executed, and the backend reported a failure.</summary>
    Failed,
}

/// <param name="Reason">Why it was rejected or why the target counts as changed.</param>
/// <param name="Snapshot">The snapshot after the operation, or null if it couldn't be read.</param>
public sealed record OperationResult(
    OperationOutcome Outcome,
    Reason? Reason = null,
    StorageError? Error = null,
    StorageSnapshot? Snapshot = null)
{
    /// <summary>True when nothing on disk can have been modified.</summary>
    public bool NothingModified => Outcome is OperationOutcome.Rejected or OperationOutcome.TargetChanged
        || (Outcome == OperationOutcome.Failed && Error is { MayHaveChanged: false });
}

/// <summary>
/// Runs confirmed operations one at a time (spec operation-execution, design D7): re-reads the
/// target, re-checks identity, fingerprint and availability, executes, then always refreshes.
/// </summary>
public sealed class OperationRunner(IStorageProvider provider)
{
    private int _busy;

    /// <summary>The backend, for dialogs that query it (supported sizes, cluster sizes).</summary>
    public IStorageProvider Provider => provider;

    public event EventHandler? BusyChanged;

    public bool IsBusy => Volatile.Read(ref _busy) == 1;

    /// <summary>The operation currently running, if any.</summary>
    public OperationRequest? Current { get; private set; }

    public async Task<OperationResult> RunAsync(
        OperationRequest request,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            return new OperationResult(OperationOutcome.Rejected, Reason.Of(ReasonCode.OperationInProgress));
        }

        Current = request;
        BusyChanged?.Invoke(this, EventArgs.Empty);
        try
        {
            var before = await provider.ReadSnapshotAsync(cancellationToken).ConfigureAwait(false);

            if (TargetFingerprint.Of(before, request.Target) != request.Fingerprint)
            {
                return new OperationResult(OperationOutcome.TargetChanged, Reason.Of(ReasonCode.TargetChanged), Snapshot: before);
            }

            var state = ActionAvailability.Evaluate(before, request.Kind, request.Target, isBusy: false);
            if (!state.IsEnabled)
            {
                return new OperationResult(OperationOutcome.Rejected, state.DisabledReason, Snapshot: before);
            }

            StorageError? error = null;
            try
            {
                await provider.ExecuteAsync(request, progress).ConfigureAwait(false);
            }
            catch (StorageOperationException ex)
            {
                error = ex.Error;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                error = new StorageError(ex.Message, MayHaveChanged: true, Details: ex.ToString());
            }

            var after = await TryReadSnapshotAsync().ConfigureAwait(false);
            if (error is not null && after is not null && after.FindDisk(DiskOf(request.Target)) is null)
            {
                error = DiskRemoved(request, error);
            }

            return error is null
                ? new OperationResult(OperationOutcome.Succeeded, Snapshot: after)
                : new OperationResult(OperationOutcome.Failed, Error: error, Snapshot: after);
        }
        finally
        {
            Current = null;
            Volatile.Write(ref _busy, 0);
            BusyChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static DiskId DiskOf(ActionTarget target) => target switch
    {
        DiskTarget d => d.Disk,
        PartitionTarget p => p.Partition.Disk,
        UnallocatedTarget u => u.Disk,
        _ => throw new ArgumentOutOfRangeException(nameof(target)),
    };

    /// <summary>
    /// Spec operation-execution "Disk removed mid-operation": the failure was the disk going away, so say
    /// that instead of Windows' generic error. The original error stays in the technical details.
    /// </summary>
    private static StorageError DiskRemoved(OperationRequest request, StorageError original)
    {
        var formatting = request is FormatVolumeRequest or CreatePartitionRequest { Format: not null };
        var details = string.Join(Environment.NewLine, new[] { original.Message, original.Details }.Where(t => !string.IsNullOrEmpty(t)));
        return new StorageError(
            Text.Texts.Get(formatting ? "Error.DiskRemovedDuringFormat" : "Error.DiskRemoved"),
            MayHaveChanged: true,
            original.Code,
            details);
    }

    private async Task<StorageSnapshot?> TryReadSnapshotAsync()
    {
        try
        {
            return await provider.ReadSnapshotAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }
}
