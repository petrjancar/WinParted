namespace WinParted.Core.Presentation;

/// <summary>
/// Widths of layout-bar segments: proportional to size, but never below a minimum so tiny
/// partitions stay clickable (64 px; unallocated 96 px), per the design's Tokens board.
/// </summary>
public static class SegmentWidths
{
    public const double MinPartitionWidth = 64;
    public const double MinUnallocatedWidth = 96;

    /// <param name="sizes">Segment sizes in bytes, in order.</param>
    /// <param name="isUnallocated">Whether each segment is unallocated space.</param>
    /// <param name="width">Total width available for the bar.</param>
    /// <param name="gap">Space between segments.</param>
    /// <returns>Widths per segment, and whether any was enlarged (bar not to scale).</returns>
    public static (double[] Widths, bool NotToScale) Compute(IReadOnlyList<ulong> sizes, IReadOnlyList<bool> isUnallocated, double width, double gap)
    {
        var n = sizes.Count;
        if (n == 0)
        {
            return ([], false);
        }

        var available = Math.Max(0, width - (gap * (n - 1)));
        var widths = new double[n];
        var fixedWidth = new bool[n];
        var enlarged = false;

        // Repeatedly give minimum widths to segments that would be too small, then share the rest.
        for (var pass = 0; pass < n + 1; pass++)
        {
            double remaining = available;
            double remainingSize = 0;
            for (var i = 0; i < n; i++)
            {
                if (fixedWidth[i])
                {
                    remaining -= widths[i];
                }
                else
                {
                    remainingSize += sizes[i];
                }
            }

            var changed = false;
            for (var i = 0; i < n; i++)
            {
                if (fixedWidth[i])
                {
                    continue;
                }

                var min = isUnallocated[i] ? MinUnallocatedWidth : MinPartitionWidth;
                var proportional = remainingSize > 0 ? Math.Max(0, remaining) * sizes[i] / remainingSize : min;
                if (proportional < min)
                {
                    widths[i] = min;
                    fixedWidth[i] = true;
                    enlarged = true;
                    changed = true;
                }
                else
                {
                    widths[i] = proportional;
                }
            }

            if (!changed)
            {
                break;
            }
        }

        return (widths, enlarged);
    }
}
