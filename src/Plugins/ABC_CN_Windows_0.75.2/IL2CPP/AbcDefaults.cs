using System.Buffers.Binary;
using Il2CppInspector.Next.Metadata;
using static Il2CppInspector.MoraxReader;

namespace Il2CppInspector.Plugin.ABC;

internal sealed partial class AbcMetadata
{
    private void ReadDefaultsAndAttributes()
    {
        // GetFieldDefaultValue 0x1807D14B0 and GetParameterDefaultValue 0x1807D22B0.
        Metadata.FieldDefaultValues = Records(
            _data,
            0x175B940,
            0x50BA,
            12,
            static (d, _) =>
                new Il2CppFieldDefaultValue
                {
                    DataIndex = I32(d, 0) ^ 0x1F498B6B,
                    TypeIndex = unchecked(I32(d, 4) - 0x6C87F1ED),
                    FieldIndex = unchecked(I32(d, 8) - 0x0936FA2C),
                }
        );
        Metadata.ParameterDefaultValues = Records(
            _data,
            0xCEE784,
            0x1C3E,
            12,
            static (d, _) =>
                new Il2CppParameterDefaultValue
                {
                    ParameterIndex = I32(d, 0) ^ 0x0BE87C9F,
                    TypeIndex = I32(d, 4) ^ 0x7F8654B1,
                    DataIndex = unchecked(I32(d, 8) - 0x1FDE16FE),
                }
        );
        // GetCustomAttributeTypeToken 0x1807F5EB0 and GetCustomAttributeDataReader 0x1807F6100.
        var count = checked((int)Metadata.Images.Max(i => (uint)i.CustomAttributeStart + i.CustomAttributeCount));
        CheckRange(_data, 8, checked((count + 1) * 8));
        var ranges = new byte[(count + 1) * 8];
        for (var i = 0; i <= count; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(ranges.AsSpan(i * 8), unchecked(U32(_data, 8 + i * 8) - 0x6BFC7EF2));
            var offset = U32(_data, 12 + i * 8) ^ 0x6B0B3E62;
            if (offset > 0x16D8B4C - 0x1672AB0 || (i > 0 && offset < (U32(_data, 4 + i * 8) ^ 0x6B0B3E62)))
                throw new InvalidDataException($"ABC attribute range {i} exceeds the attribute data section.");
            BinaryPrimitives.WriteUInt32LittleEndian(ranges.AsSpan(i * 8 + 4), offset);
        }
        Metadata.Position = Metadata.Length;
        var start = Metadata.Position;
        Metadata.Write(ranges);
        Metadata.AttributeDataRanges = Metadata.ReadVersionedObjectArray<Il2CppCustomAttributeDataRange>(start, count + 1);
        Metadata.AttributeTypeRanges = [];
        Metadata.AttributeTypeIndices = [];
        Metadata.MetadataUsageLists = [];
        Metadata.MetadataUsagePairs = [];
        Metadata.TypeInlineArrays = [];
    }

    private void ReadLiterals()
    {
        // GetStringLiteralFromIndex 0x1807F9D80 decrypts UTF-8 blocks with a per-literal key (0x1807AC050).
        const int count = (0x171F068 - 0x16DE138) / 8;
        Metadata.StringLiterals = new string[count];
        for (var i = 0; i < count; i++)
        {
            var offset = unchecked(I32(_data, 0x16DE138 + i * 8) - 0x368B8FD5);
            var length = unchecked(I32(_data, 0x16DE13C + i * 8) - 0x4E93B1A0);
            if (offset < 0 || length < 0 || offset > 0x57DEB4 - 0x49B9F0 || length > 0x57DEB4 - 0x49B9F0 - offset)
                throw new InvalidDataException($"ABC string literal {i} exceeds its encrypted data section.");
            var key = unchecked(
                0xB46E60EA571EC6F8UL * ((0x240336DF2D460D6EUL * (ulong)i + 0x388589644E3EC8DCUL) ^ 0x0D1B43DE74DF974EUL) + 0x4CE743955962AB10UL
            );
            Metadata.StringLiterals[i] = DecodeBlocks(_data, checked(0x49B9F0 + offset), length, key, 0x2BDAD5153595DF43);
        }
    }

    public override (ulong Address, object Value) DecodeDefault(int typeIndex, int dataIndex, Il2CppBinary binary)
    {
        if (dataIndex == -1)
            return (0, null);
        var offset = checked(0x3BCA70 + dataIndex);
        if (offset < 0x3BCA70 || offset >= 0x3E65A0)
            throw new InvalidDataException($"ABC default value data index {dataIndex} exceeds its section.");
        return ((ulong)offset, DecodeConstant(Metadata, offset, binary.TypeReferences[typeIndex].Type));
    }
}
