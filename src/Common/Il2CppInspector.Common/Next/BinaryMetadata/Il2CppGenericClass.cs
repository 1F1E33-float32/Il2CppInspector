using VersionedSerialization;

namespace Il2CppInspector.Next.BinaryMetadata;

public record struct Il2CppGenericClass : IReadable
{
    public int TypeDefinitionIndex;

    public Pointer<Il2CppType> Type;

    public Il2CppGenericContext Context;

    public PrimitivePointer<byte> CachedClass; // Il2CppClass*, optional

    public void Read<TReader>(ref Reader<TReader> reader, in StructVersion version = default)
        where TReader : IReader, allows ref struct
    {
        if (version < MetadataVersions.V270)
            TypeDefinitionIndex = (int)reader.ReadNativeInt();
        else
            Type = reader.ReadNativeUInt();
        // Unity 6000.6 removes the always-null method_inst pointer from generic class contexts.
        Context = new Il2CppGenericContext { ClassInst = reader.ReadNativeUInt() };
        if (version < MetadataVersions.V1080)
            Context.MethodInst = reader.ReadNativeUInt();
        CachedClass = reader.ReadNativeUInt();
    }

    public static int Size(in StructVersion version = default, in ReaderConfig config = default) =>
        (version < MetadataVersions.V1080 ? 4 : 3) * (config.Is32Bit ? 4 : 8);
}
