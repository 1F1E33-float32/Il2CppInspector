using VersionedSerialization;

namespace Il2CppInspector.Next;

public static class MetadataVersions
{
    // Unity 5.3.1p1-p2 introduces metadata usages before adding TypeDef/Image tokens in version 19.
    public static readonly StructVersion V180 = new(18);

    public static readonly StructVersion V190 = new(19);

    // Unity 5.3.4p1 adds CCW indices before the metadata version changes to 21.
    public static readonly StructVersion V201 = new(20, 1);

    public static readonly StructVersion V210 = new(21);
    public static readonly StructVersion V220 = new(22);

    public static readonly StructVersion V240 = new(24);
    public static readonly StructVersion V241 = new(24, 1);
    public static readonly StructVersion V242 = new(24, 2);
    public static readonly StructVersion V243 = new(24, 3);
    public static readonly StructVersion V244 = new(24, 4);
    public static readonly StructVersion V245 = new(24, 5);

    public const string Tag2018 = "2018"; // Unity 2018.4.18+ backports the Windows Runtime factory table to 24.1.

    public static readonly StructVersion V270 = new(27);
    public static readonly StructVersion V271 = new(27, 1);
    public static readonly StructVersion V272 = new(27, 2);

    // These two versions have two variations:
    public static readonly StructVersion V290 = new(29);
    public static readonly StructVersion V310 = new(31);

    // No tag - 29.0/31.0
    public const string Tag2022 = "2022"; // 29.1/31.1

    // Unity 6000.3.0f1 uses section descriptors and tags for TypeIndex, TypeDefinitionIndex,
    // GenericContainerIndex and ParameterIndex widths.
    public static readonly StructVersion V390 = new(39);

    // Unity 6000.5.0f1 adds inline arrays, compact generic containers and tags for the remaining index widths:
    // InterfacesIndex, EventIndex, PropertyIndex, NestedTypeIndex, MethodIndex, GenericParameterIndex,
    // FieldIndex and DefaultValueDataIndex.
    public static readonly StructVersion V1060 = new(106);

    // Unity 6000.6.0f1 stores generic method tables, invoker indices and RGCTX in global metadata.
    public static readonly StructVersion V1080 = new(108);
}
