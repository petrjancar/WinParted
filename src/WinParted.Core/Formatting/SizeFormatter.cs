using System.Globalization;

namespace WinParted.Core.Formatting;

/// <summary>
/// Formats byte counts the way File Explorer does: binary multiples labelled KB/MB/GB/TB, three
/// significant digits, truncated rather than rounded (design D10).
/// </summary>
public static class SizeFormatter
{
    private static readonly string[] Units = ["KB", "MB", "GB", "TB", "PB", "EB"];

    public static string Format(ulong bytes, IFormatProvider? provider = null)
    {
        provider ??= CultureInfo.CurrentCulture;

        if (bytes < 1000)
        {
            return string.Format(provider, "{0} bytes", bytes);
        }

        double value = bytes;
        var unit = -1;
        while (value >= 1000 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        string number = value switch
        {
            >= 100 => Math.Truncate(value).ToString("0", provider),
            >= 10 => (Math.Truncate(value * 10) / 10).ToString("0.0", provider),
            _ => (Math.Truncate(value * 100) / 100).ToString("0.00", provider),
        };

        return $"{number} {Units[unit]}";
    }
}
