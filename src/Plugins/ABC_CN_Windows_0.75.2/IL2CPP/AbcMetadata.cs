using System.Buffers.Binary;
using System.Text;
using Il2CppInspector.Next;
using Il2CppInspector.Next.Metadata;
using Il2CppInspector.Plugins;
using static Il2CppInspector.MoraxReader;

namespace Il2CppInspector.Plugin.ABC;

internal sealed partial class AbcMetadata : MoraxAdapter
{
    private readonly byte[] _data;
    private readonly Dictionary<ulong, int> _strings = [];
    internal Metadata Metadata { get; }
    internal Il2CppBinary Binary { get; }
    public override List<MetadataUsage> Usages { get; } = [];
    public override int VTableSlotSize => 16;
    public override int AttributeDataOffset => 0x1672AB0;

    internal AbcMetadata(IFileFormatStream image, byte[] data, GamePlugin plugin, EventHandler<string> status)
        : base(image, plugin)
    {
        _data = data;
        Metadata = Metadata.CreateForPlugin(data, this, MetadataVersions.V290, status);
        // 0x1807D0BF0: 116105 type pointers, 30988 definitions and 221780 methods.
        var firstType = image.ReadMappedUInt64(image.ImageBase + 0x157CDB0);
        if (firstType != image.ImageBase + 0x1ED85C0 || image.ReadMappedUInt32(firstType + 8) != 0x001E0000)
            throw new NotSupportedException("ABC binary does not match the verified 0.75.2 Windows runtime tables.");
        ReadMetadata();
        var instancePointers = image.ReadMappedUWordArray(image.ImageBase + 0xA15B70, 21843);
        _genericInstances = instancePointers.Select((p, i) => (p, i)).ToDictionary(p => p.p, p => p.i);
        _genericClasses = image.ReadMappedUWordArray(image.ImageBase + 0x95BCE0, 0xC92C);
        Binary = new Il2CppBinaryX64(image, status);
        ReadBinary(instancePointers);
    }

    private int Intern(ulong value)
    {
        if (_strings.TryGetValue(value, out var index))
            return index;
        var text = DecodeString(value);
        index = Metadata.Strings.Count;
        Metadata.Strings.Add(index, text);
        _strings.Add(value, index);
        return index;
    }

    private string DecodeString(ulong value)
    {
        // 0x1807CF590 returns signed identifiers unchanged; 0x1807D35C0 formats them as uppercase hex.
        if ((long)value < 0)
            return AbcNames.DecodeIdentifier(value);
        if (value == uint.MaxValue)
            return "";
        var length = (int)(value >> 24 & 255);
        var offset = checked(0xE69408 + (int)(value & 0xFFFFFF));
        var rounded = (length + 7) & ~7;
        if (offset > 0xEE8930 - rounded)
            throw new InvalidDataException("ABC string identifier exceeds the encrypted string section.");
        Span<byte> bytes = stackalloc byte[rounded];
        var state = (uint)(value ^ (value >> 32));
        for (var i = 0; i < rounded; i += 8)
        {
            var key = unchecked(
                ((0x7784DFB7188F3623UL * (0x67FE1D5322AE5717UL * state ^ 0x235E971E0741E578UL)) ^ 0x4CD3160E40DC1425UL)
                + (i == 0 ? 0x6CF6A6BB174F37A4UL : 0x759BF4F5407E7879UL)
            );
            BinaryPrimitives.WriteUInt64LittleEndian(bytes[i..], U64(_data, offset + i) ^ key);
            state = unchecked(state + 0x292F40D5);
        }
        return Encoding.UTF8.GetString(bytes[..length]);
    }

    private void ReadMetadata()
    {
        Metadata.Types = Records(_data, 0x101DE80, 30988, 92, ReadType);
        Metadata.Methods = Records(_data, 0x617BE4, 221780, 32, ReadMethod);
        var types = Metadata.Types;
        var fieldCount = types.Max(t => t.FieldCount == 0 ? 0 : (int)t.FieldIndex + t.FieldCount);
        var propertyCount = types.Max(t => t.PropertyCount == 0 ? 0 : (int)t.PropertyIndex + t.PropertyCount);
        var eventCount = types.Max(t => t.EventCount == 0 ? 0 : (int)t.EventIndex + t.EventCount);
        var paramCount = Metadata.Methods.Max(m => m.ParameterCount == 0 ? 0 : (int)m.ParameterStart + m.ParameterCount);
        if (
            fieldCount > (0x149B8A4 - 0x12EC390) / 12
            || propertyCount > (0x3BCA70 - 0x2F77D4) / 20
            || eventCount > (0x4066E0 - 0x4049D0) / 24
            || paramCount > (0x2E3BD0 - 0xBB318) / 12
        )
            throw new InvalidDataException("ABC metadata member counts exceed their record sections.");
        // SetupFields 0x18078D150, GetParameterName 0x1807A2300.
        Metadata.Fields = Records(
            _data,
            0x12EC390,
            fieldCount,
            12,
            (d, i) =>
                new Il2CppFieldDefinition
                {
                    TypeIndex = I32(d, 0) ^ 0x1B9E3B16,
                    NameIndex = Intern(U64(d, 4) ^ 0x53F45BDD3D64E178),
                    Token = 0x04000001u + (uint)i,
                }
        );
        Metadata.Params = Records(
            _data,
            0xBB318,
            paramCount,
            12,
            (d, i) =>
                new Il2CppParameterDefinition
                {
                    NameIndex = Intern(U64(d, 0) ^ 0x4ABFC82A430A41F6),
                    TypeIndex = unchecked(I32(d, 8) - 0x1CA16046),
                    Token = 0x08000001u + (uint)i,
                }
        );
        Metadata.Properties = Records(_data, 0x2F77D4, propertyCount, 20, ReadProperty);
        Metadata.Events = Records(_data, 0x4049D0, eventCount, 24, ReadEvent);
        Metadata.GenericContainers = Records(
            _data,
            0x1014000,
            0x9E8,
            16,
            static (d, _) =>
                new Il2CppGenericContainer
                {
                    IsMethod = (I32(d, 0) ^ 0x2A367B99) == 0 ? 0 : 1,
                    GenericParameterStart = I32(d, 4) ^ 0x2E1A91E0,
                    TypeArgc = I32(d, 8) ^ 0x2F311381,
                    OwnerIndex = I32(d, 12) ^ 0x53A44EB0,
                }
        );
        var genericParameters = Metadata.GenericContainers.Max(c => (int)c.GenericParameterStart + c.TypeArgc);
        // GenericParameterInfo 0x180790720: name +4, constraints +C, flags +E, count +10, ordinal +12.
        Metadata.GenericParameters = Records(
            _data,
            0x2E3BD0,
            genericParameters,
            20,
            (d, _) =>
                new Il2CppGenericParameter
                {
                    OwnerIndex = I32(d, 0) ^ 0x3906BBA3,
                    NameIndex = Intern(unchecked(U64(d, 4) - 0x56DDE46C0EBD17F2)),
                    ConstraintsStart = unchecked((short)(U16(d, 12) ^ 0x9B8A)),
                    Flags = unchecked((ushort)(U16(d, 14) + 0x2262)),
                    ConstraintsCount = unchecked((short)(U16(d, 16) ^ 0xA11F)),
                    Num = unchecked((ushort)(U16(d, 18) - 0x6D2B)),
                }
        );
        var constraints = Metadata.GenericParameters.Max(p => p.ConstraintsCount == 0 ? 0 : p.ConstraintsStart + p.ConstraintsCount);
        Metadata.GenericConstraintIndices = Records(_data, 0x61651C, constraints, 4, static (d, _) => new TypeIndex(I32(d, 0)));
        Metadata.NestedTypeIndices = Records(
            _data,
            0x1753FC0,
            types.Max(t => t.NestedTypeCount == 0 ? 0 : (int)t.NestedTypeIndex + t.NestedTypeCount),
            4,
            static (d, _) => I32(d, 0)
        );
        Metadata.InterfaceUsageIndices = Records(
            _data,
            0x1723398,
            types.Max(t => t.InterfacesCount == 0 ? 0 : (int)t.InterfacesIndex + t.InterfacesCount),
            4,
            static (d, _) => new TypeIndex(I32(d, 0))
        );
        Metadata.InterfaceOffsets = Records(
            _data,
            0x1728C00,
            types.Max(t => t.InterfaceOffsetsCount == 0 ? 0 : (int)t.InterfaceOffsetsStart + t.InterfaceOffsetsCount),
            8,
            static (d, _) => new Il2CppInterfaceOffsetPair { InterfaceTypeIndex = I32(d, 0), Offset = I32(d, 4) }
        );
        Metadata.VTableMethodIndices = Records(
            _data,
            0xD04B04,
            types.Max(t => t.VTableCount == 0 ? 0 : t.VTableIndex + t.VTableCount),
            4,
            static (d, _) => NormalizeUsage(U32(d, 0))
        );
        ReadAssemblies();
        ReadDefaultsAndAttributes();
        ReadLiterals();
    }
}
