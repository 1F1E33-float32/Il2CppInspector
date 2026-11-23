using System.Reflection;
using System.Runtime.InteropServices;
using Il2CppInspector.Next.Metadata;
using static Il2CppInspector.MoraxReader;

namespace Il2CppInspector.Plugin.ABC;

internal sealed partial class AbcMetadata
{
    // GlobalMetadata::GetTypeInfoFromTypeDefinitionIndex, 0x1807D03A0.
    private Il2CppTypeDefinition ReadType(ReadOnlySpan<byte> d, int i)
    {
        var bits = U32(d, 0x58) ^ 0x0AB2E12Fu;
        return new()
        {
            Flags = (TypeAttributes)(U32(d, 0) ^ 0x37895FCE),
            ParentIndex = unchecked(I32(d, 4) - 0x4CDEDEF7),
            GenericContainerIndex = I32(d, 8) ^ 0x1625656E,
            NamespaceIndex = Intern(unchecked(U64(d, 0x10) - 0x6D7EB179439FB68D)),
            DeclaringTypeIndex = I32(d, 0x18) ^ 0x32B7BCDB,
            FieldIndex = I32(d, 0x1C) ^ 0x783B7541,
            EventIndex = I32(d, 0x20) ^ 0x0468CACC,
            NestedTypeIndex = I32(d, 0x24) ^ 0x3770AD59,
            MethodIndex = I32(d, 0x28) ^ 0x0332EEC1,
            ElementTypeIndex = I32(d, 0x2C) ^ 0x43B5164B,
            NameIndex = Intern(U64(d, 0x30) ^ 0x5A7CF3EB727A82E7),
            ByValTypeIndex = unchecked(I32(d, 0x38) - 0x3A460867),
            ByRefTypeIndex = -1,
            VTableIndex = unchecked(I32(d, 0x3C) - 0x0FEBD3B3),
            InterfacesIndex = unchecked(I32(d, 0x40) - 0x655822BC),
            PropertyIndex = unchecked(I32(d, 0x44) + (int)0x8A860C07),
            InterfaceOffsetsStart = unchecked(I32(d, 12) - 0x55A166E9),
            NestedTypeCount = unchecked((ushort)(U16(d, 0x4A) - 0x23F4)),
            FieldCount = unchecked((ushort)(U16(d, 0x48) + 0x53D0)),
            EventCount = (ushort)(U16(d, 0x4C) ^ 0xCCE5),
            MethodCount = unchecked((ushort)(U16(d, 0x4E) - 0x35D9)),
            InterfacesCount = unchecked((ushort)(U16(d, 0x50) + 0x42B8)),
            PropertyCount = unchecked((ushort)(U16(d, 0x52) - 0x26AB)),
            InterfaceOffsetsCount = unchecked((ushort)(U16(d, 0x54) - 0x7F79)),
            VTableCount = (ushort)(U16(d, 0x56) ^ 0x72E9),
            Bitfield = Il2CppTypeDefinitionBitfield.FromRawValue((bits & 0x1B) | (bits >> 3 & 4) | (bits << 3 & 0x20) | ((bits & 0xFFC0) ^ 0xC00)),
            Token = 0x02000001u + (uint)i,
        };
    }

    // Class::SetupMethods, 0x18078D490; method tokens are global definition ordinals (0x1807F5000).
    private Il2CppMethodDefinition ReadMethod(ReadOnlySpan<byte> d, int i) =>
        new()
        {
            ParameterStart = unchecked(I32(d, 0) - 0x44A8F407),
            NameIndex = Intern(unchecked(U64(d, 4) - 0x34637D8A4AD31D59)),
            ReturnType = I32(d, 12) ^ 0x208E3B64,
            GenericContainerIndex = I32(d, 16) ^ 0x251108B1,
            DeclaringType = unchecked(I32(d, 20) - 0x5CE89C4A),
            ImplFlags = unchecked((ushort)(U16(d, 24) - 0x7090)),
            Flags = unchecked((ushort)(U16(d, 26) + 0x02D5)),
            Slot = unchecked((ushort)(U16(d, 28) - 0x4613)),
            ParameterCount = unchecked((byte)(d[30] + 0x61)),
            Token = 0x06000001u + (uint)i,
            MethodIndex = i,
            InvokerIndex = U16(_data, 0x149B8A4 + i * 2) == ushort.MaxValue ? -1 : U16(_data, 0x149B8A4 + i * 2),
        };

    // Class::SetupProperties (0x18078EF20) and SetupEvents (0x18078EBF0).
    private Il2CppPropertyDefinition ReadProperty(ReadOnlySpan<byte> d, int i) =>
        new()
        {
            Attrs = (PropertyAttributes)(U32(d, 0) ^ 0x26136C21),
            Set = I32(d, 4) ^ 0x13A2C7BF,
            Get = unchecked(I32(d, 8) - 0x7C0D5AF8),
            NameIndex = Intern(U64(d, 12) ^ 0x4B02BFEE568DE517),
            Token = 0x17000001u + (uint)i,
        };

    private Il2CppEventDefinition ReadEvent(ReadOnlySpan<byte> d, int i) =>
        new()
        {
            Remove = I32(d, 0) ^ 0x68C4E0B5,
            Add = unchecked(I32(d, 4) - 0x1018FE61),
            TypeIndex = I32(d, 8) ^ 0x35DEC4EE,
            Raise = unchecked(I32(d, 12) - 0x2EA81243),
            NameIndex = Intern(unchecked(U64(d, 16) - 0x617B407F1C2E280E)),
            Token = 0x14000001u + (uint)i,
        };

    private static uint NormalizeUsage(uint value)
    {
        var kind = value >> 28;
        var index = value & 0x0FFFFFFF;
        if (value is 0 or 1)
            return 0;
        return kind is 3 or 6 ? kind << 29 | index << 1 : throw new InvalidDataException($"ABC vtable reference has unsupported kind {kind}.");
    }

    private void ReadAssemblies()
    {
        // MetadataCache::Initialize, 0x18079E950; BuildIl2CppAssembly, 0x1807D2120.
        Metadata.Images = Records(
            _data,
            0x4066E0,
            97,
            40,
            (d, i) =>
                new Il2CppImageDefinition
                {
                    NameIndex = Intern(unchecked(U64(d, 0) - 0x395B49597FBA3913)),
                    AssemblyIndex = I32(d, 28) ^ 0x562395D7,
                    TypeStart = I32(d, 20) ^ 0x751B57D9,
                    TypeCount = unchecked(U32(d, 24) - 0x78FC374B),
                    ExportedTypeStart = unchecked(I32(d, 16) - 0x4FB7D9D5),
                    ExportedTypeCount = unchecked(U32(d, 32) - 0x63EA7398),
                    EntryPointIndex = unchecked(I32(d, 12) - 0x1B514236),
                    CustomAttributeStart = I32(d, 36) ^ 0x5560B5CA,
                    CustomAttributeCount = unchecked(U32(d, 8) - 0x48C43731),
                    Token = 1u + (uint)i,
                }
        );
        Metadata.Assemblies = Records(
            _data,
            0x1507D4C,
            97,
            60,
            (d, i) =>
            {
                var image = _data.AsSpan(0x16D8B4C + i * 12, 12);
                var name = new Il2CppAssemblyNameDefinition
                {
                    NameIndex = Intern(U64(d, 12) ^ 0x28F2EE5C6ACCEF27),
                    CultureIndex = Intern(U64(d, 24) ^ 0x1C8BFA11133527FD),
                    PublicKeyIndex = Intern(unchecked(U64(d, 32) - 0x3541505864046D7A)),
                    HashAlg = (AssemblyHashAlgorithm)(U32(d, 8) ^ 0x4E06C8F1),
                    HashLen = I32(d, 4) ^ 0x448E79FF,
                    Flags = unchecked((AssemblyNameFlags)(U32(d, 40) - 0x420580EF)),
                    Major = I32(d, 20) ^ 0x778A0C28 ^ 0x6452FB43,
                    Minor = unchecked(I32(d, 44) - 0x0A866BED),
                    Build = unchecked(I32(d, 0) - 0x068A19B0),
                    Revision = unchecked(I32(d, 48) - 0x220D6065),
                };
                d.Slice(52, 8).CopyTo(MemoryMarshal.CreateSpan(ref name.PublicKeyToken[0], 8));
                return new Il2CppAssemblyDefinition
                {
                    ImageIndex = unchecked(I32(image, 0) - 0x6CE6CE79),
                    Token = 0x20000001u + (uint)i,
                    ReferencedAssemblyStart = unchecked(I32(image, 4) - 0x7466815B),
                    ReferencedAssemblyCount = unchecked((I32(image, 8) ^ 0x73E7A75A) - 0x1DF66184),
                    Aname = name,
                };
            }
        );
    }
}
