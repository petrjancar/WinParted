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
        _ => StorageErrorKind.Unknown,
    };

    /// <summary>Whether a failure of this kind can have left the disk partly changed.</summary>
    public static bool MayHaveChanged(StorageErrorKind kind) =>
        kind is StorageErrorKind.Unknown or StorageErrorKind.CreatedButFormatFailed;

    public static StorageError Map(uint code, string method, string? extendedMessage = null, bool fullFormat = false) =>
        Create(Classify(code, method, fullFormat), code, $"{method} returned {code}", extendedMessage);

    public static StorageError Create(StorageErrorKind kind, uint? code, string operation, string? extendedMessage = null)
    {
        var details = string.Join(
            Environment.NewLine,
            new[] { operation, extendedMessage is { Length: > 0 } ? $"Message: {extendedMessage}" : null }.OfType<string>());
        return new StorageError(Message(kind), MayHaveChanged(kind), code, details);
    }

    public static string Message(StorageErrorKind kind) =>
        string.Format(CultureInfo.CurrentCulture, Texts.Get($"Error.{kind}"));
}
