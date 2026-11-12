namespace WinParted.Core.Model;

/// <summary>Well-known GPT partition type GUIDs and MBR partition type bytes.</summary>
public static class KnownPartitionTypes
{
    public static readonly Guid GptEfiSystem = new("c12a7328-f81f-11d2-ba4b-00a0c93ec93b");
    public static readonly Guid GptMicrosoftReserved = new("e3c9e316-0b5c-4db8-817d-f92df00215ae");
    public static readonly Guid GptBasicData = new("ebd0a0a2-b9e5-4433-87c0-68b6b72699c7");
    public static readonly Guid GptRecovery = new("de94bba4-06d1-4d40-a16a-bfd50179d6ac");
    public static readonly Guid GptLdmMetadata = new("5808c8aa-7e8f-42e0-85d2-e1e90434cfb3");
    public static readonly Guid GptLdmData = new("af9b60a0-1431-4f62-bc68-3311714a69ad");
    public static readonly Guid GptStorageSpaces = new("e75caf8f-f680-4cee-afa3-b001e56efc2d");

    public const byte MbrEfiSystem = 0xEF;
    public const byte MbrRecovery = 0x27;
    public const byte MbrLdm = 0x42;

    private static readonly HashSet<byte> MbrBasicDataTypes = [0x01, 0x04, 0x06, 0x07, 0x0B, 0x0C, 0x0E];
    private static readonly HashSet<byte> MbrExtendedTypes = [0x05, 0x0F, 0x85];

    public static bool IsEfiSystem(Partition p) => p.GptType == GptEfiSystem || p.MbrType == MbrEfiSystem;

    public static bool IsMicrosoftReserved(Partition p) => p.GptType == GptMicrosoftReserved;

    public static bool IsRecovery(Partition p) => p.GptType == GptRecovery || p.MbrType == MbrRecovery;

    public static bool IsLdm(Partition p) =>
        p.GptType == GptLdmMetadata || p.GptType == GptLdmData || p.MbrType == MbrLdm;

    public static bool IsStorageSpaces(Partition p) => p.GptType == GptStorageSpaces;

    public static bool IsBasicData(Partition p) =>
        p.GptType == GptBasicData || (p.MbrType is byte t && MbrBasicDataTypes.Contains(t));

    /// <summary>
    /// MBR extended partition containers. They hold logical partitions and are not shown as segments.
    /// </summary>
    public static bool IsMbrExtendedContainer(Partition p) => p.MbrType is byte t && MbrExtendedTypes.Contains(t);

    public static bool IsReserved(Partition p) =>
        IsEfiSystem(p) || IsMicrosoftReserved(p) || IsRecovery(p) || IsLdm(p) || IsStorageSpaces(p);
}
