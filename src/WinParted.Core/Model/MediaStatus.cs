namespace WinParted.Core.Model;

/// <summary>Reads "no media" from what Windows reports for a disk (change empty-card-reader-slots, D1).</summary>
public static class MediaStatus
{
    /// <summary>SMP <c>MSFT_Disk.OperationalStatus</c> value "No Media".</summary>
    public const ushort NoMediaStatus = 0xD012;

    /// <summary>
    /// True for a drive without media, such as an empty card reader slot: its operational status
    /// includes "No Media", or it reports a size of 0 (some readers report "Not Ready" instead, and
    /// a 0-byte disk can't be used for anything anyway).
    /// </summary>
    public static bool IsMissing(IEnumerable<ushort>? operationalStatus, ulong size) =>
        size == 0 || (operationalStatus?.Contains(NoMediaStatus) ?? false);
}
