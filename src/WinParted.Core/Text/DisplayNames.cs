using System.Globalization;
using WinParted.Core.Model;

namespace WinParted.Core.Text;

public static class DisplayNames
{
    /// <summary>
    /// The title shown on a partition's segment and in dialogs: "Data (D:)", "C:", "Data", or a
    /// type name such as "Recovery" or "Unknown filesystem".
    /// </summary>
    public static string ForPartition(Partition partition)
    {
        var label = partition.Volume?.Label;
        var hasLabel = !string.IsNullOrWhiteSpace(label);

        return (hasLabel, partition.DriveLetter) switch
        {
            (true, char letter) => $"{label} ({letter}:)",
            (false, char letter) => $"{letter}:",
            (true, null) => label!,
            _ => TypeName(partition),
        };
    }

    /// <summary>The partition's type name, ignoring label and letter.</summary>
    public static string TypeName(Partition partition)
    {
        if (KnownPartitionTypes.IsEfiSystem(partition))
        {
            return Texts.Get("Type.EfiSystem");
        }

        if (KnownPartitionTypes.IsMicrosoftReserved(partition))
        {
            return Texts.Get("Type.MicrosoftReserved");
        }

        if (KnownPartitionTypes.IsRecovery(partition))
        {
            return Texts.Get("Type.Recovery");
        }

        if (KnownPartitionTypes.IsLdm(partition))
        {
            return Texts.Get("Type.DynamicDisk");
        }

        if (KnownPartitionTypes.IsStorageSpaces(partition))
        {
            return Texts.Get("Type.StorageSpaces");
        }

        return partition.Content switch
        {
            PartitionContent.Unformatted => Texts.Get("Type.Unformatted"),
            PartitionContent.Foreign => Texts.Get("Type.UnknownFileSystem"),
            _ => string.Format(CultureInfo.CurrentCulture, Texts.Get("Type.Partition"), partition.Number),
        };
    }

    /// <summary>
    /// How the selected volume is named as the subject of a reason: the drive letter alone when it
    /// has one ("D: can only grow…"), otherwise its title.
    /// </summary>
    public static string ForPartitionAsSubject(Partition partition) =>
        partition.DriveLetter is char letter ? $"{letter}:" : ForPartition(partition);

    /// <summary>
    /// How a neighbouring partition is named inside a sentence ("The Recovery partition is in the way").
    /// Reserved partitions use their type name even if they have a label.
    /// </summary>
    public static string ForPartitionInSentence(Partition partition) =>
        partition.Content == PartitionContent.Reserved ? TypeName(partition) : ForPartition(partition);
}
