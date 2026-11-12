using System.Globalization;
using WinParted.Core.Formatting;
using WinParted.Core.Model;
using WinParted.Core.Text;

namespace WinParted.Core.Presentation;

/// <summary>Hot-plug notifications (spec device-change-detection, mvp-features.md section 9).</summary>
public static class ChangeMessages
{
    /// <param name="selectedDisk">The disk that was selected before the refresh, if any.</param>
    /// <param name="selectedAfter">The disk selected after the refresh, if any.</param>
    public static IReadOnlyList<string> Between(StorageSnapshot before, StorageSnapshot after, DiskId? selectedDisk, DiskId? selectedAfter = null)
    {
        var messages = new List<string>();
        foreach (var added in after.Disks.Where(d => before.FindDisk(d.Id) is null))
        {
            messages.Add(F("Ui.Toast.Connected", Presenter.Bus(added.Bus), added.FriendlyName, SizeFormatter.Format(added.Size)));
        }

        foreach (var removed in before.Disks.Where(d => after.FindDisk(d.Id) is null))
        {
            messages.Add(removed.Id == selectedDisk && selectedAfter is DiskId next && after.FindDisk(next) is { } shown
                ? F("Ui.Toast.SelectedDisconnected", removed.Number, shown.Number)
                : F("Ui.Toast.Disconnected", removed.Number));
        }

        return messages;
    }

    private static string F(string key, params object[] args) => string.Format(CultureInfo.CurrentCulture, Texts.Get(key), args);
}
