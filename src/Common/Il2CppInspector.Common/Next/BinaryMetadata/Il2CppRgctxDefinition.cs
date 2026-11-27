using VersionedSerialization;

namespace Il2CppInspector.Next.BinaryMetadata;

public record struct Il2CppRgctxDefinition : IReadable
{
    public Il2CppRgctxDataType Type;

    public PrimitivePointer<byte> Data; // void*

    public readonly Pointer<Il2CppRgctxDefinitionData> Definition => Data.PointerValue;
    public readonly Pointer<Il2CppRgctxConstrainedData> Constrained => Data.PointerValue;

    public void Read<TReader>(ref Reader<TReader> reader, in StructVersion version = default)
        where TReader : IReader, allows ref struct
    {
        // Unity 2021.1 replaces the inline 32-bit RGCTX index with a pointer and aligns the preceding enum to it.
        var inlineData = version < MetadataVersions.V272 || version >= MetadataVersions.V1080;
        Type = (Il2CppRgctxDataType)(inlineData ? reader.ReadPrimitive<int>() : reader.ReadNativeInt());
        Data = inlineData ? reader.ReadPrimitive<uint>() : reader.ReadNativeUInt();
    }

    public static int Size(in StructVersion version = default, in ReaderConfig config = default) =>
        version < MetadataVersions.V272 || version >= MetadataVersions.V1080 || config.Is32Bit ? 8 : 16;
}
