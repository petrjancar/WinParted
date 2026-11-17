using System.Globalization;
using WinParted.Core.Text;

namespace WinParted.Core.Operations;

/// <summary>Plain-language categories of SMP failures (task 8.2, docs/spikes/smp-behavior.md).</summary>
public enum StorageErrorKind
{
    NotSupported,
    InvalidParameter,
    SizeNotSupported,
    NotEnoughSpace,
    AccessDenied,
    NotInitialized,
    AlreadyInitialized,
    ReadOnly,
    PartitionLimit,
    FullFormatNotSupported,
    CreatedButFormatFailed,
    DriveLetterInUse,
    CreatedButLetterFailed,
    Unknown,
}

/// <summary>
/// Turns SMP return codes into messages that say what happened, whether anything was changed, and
/// what the user can do. Text lives in Resources/Texts.resx under <c>Error.{Kind}</c>.
/// </summary>
public static class StorageErrorMapper
{
    public static StorageErrorKind Classify(uint code, string? method = null, bool fullFormat = false) => code switch
    {
        1 when method == "Format" && fullFormat => StorageErrorKind.FullFormatNotSupported,
        1 => StorageErrorKind.NotSupported,
        5 => StorageErrorKind.InvalidParameter,
        4097 => StorageErrorKind.SizeNotSupported,
        40000 => StorageErrorKind.NotEnoughSpace,
        40001 => StorageErrorKind.AccessDenied,
        41000 => StorageErrorKind.NotInitialized,
        41001 => StorageErrorKind.AlreadyInitialized,
        41002 => StorageErrorKind.ReadOnly,
        41003 => StorageErrorKind.PartitionLimit,
        42002 => StorageErrorKind.DriveLetterInUse,
        _ => StorageErrorKind.Unknown,
    };

    /// <summary>Whether a failure of this kind can have left the disk partly changed.</summary>
    public static bool MayHaveChanged(StorageErrorKind kind) =>
        kind is StorageErrorKind.Unknown or StorageErrorKind.CreatedButFormatFailed or StorageErrorKind.CreatedButLetterFailed;

    public static StorageError Map(uint code, string method, string? extendedMessage = null, bool fullFormat = false) =>
        Create(Classify(code, method, fullFormat), code, $"{method} returned {code}", extendedMessage);

    public static StorageError Create(StorageErrorKind kind, uint? code, string operation, string? extendedMessage = null)
    {
        var details = string.Join(
            Environment.NewLine,
            new[] { operation, extendedMessage is { Length: > 0 } ? $"Message: {extendedMessage}" : null }.OfType<string>());
        return new StorageError(Message(kind), MayHaveChanged(kind), code, details);
    }

    /// <summary>
    /// Change drive letter couldn't use <paramref name="letter"/> because another drive holds it
    /// (42002, "The requested access path is already in use"). Nothing was changed.
    /// </summary>
    /// <param name="currentLetter">The volume's letter, which it still has; null if it has none.</param>
    public static StorageError DriveLetterInUse(char letter, char? currentLetter, uint? code, string operation, string? extendedMessage = null)
    {
        var message = string.Format(CultureInfo.CurrentCulture, Texts.Get("Error.DriveLetterInUse.Letter"), letter);
        if (currentLetter is char current)
        {
            message += " " + string.Format(CultureInfo.CurrentCulture, Texts.Get("Error.DriveLetterInUse.Still"), current);
        }

        return Create(StorageErrorKind.DriveLetterInUse, code, operation, extendedMessage) with
        {
            Message = message,
            Advice = Texts.Get("Error.DriveLetterInUse.Action"),
        };
    }

    /// <summary>
    /// Create partition created and formatted the partition, but couldn't give it
    /// <paramref name="letter"/> because another drive holds it. The disk did change.
    /// </summary>
    public static StorageError CreatedButLetterFailed(char letter, uint? code, string operation, string? extendedMessage = null) =>
        Create(StorageErrorKind.CreatedButLetterFailed, code, operation, extendedMessage) with
        {
            Message = string.Format(CultureInfo.CurrentCulture, Texts.Get("Error.CreatedButLetterFailed.Letter"), letter),
            Changed = Texts.Get("Error.CreatedButLetterFailed.Changed"),
            Advice = Texts.Get("Error.CreatedButLetterFailed.Action"),
        };

    public static string Message(StorageErrorKind kind) =>
        string.Format(CultureInfo.CurrentCulture, Texts.Get($"Error.{kind}"));
}
