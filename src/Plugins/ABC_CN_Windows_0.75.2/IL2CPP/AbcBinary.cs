using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Il2CppInspector.Next;
using Il2CppInspector.Next.BinaryMetadata;
using Il2CppInspector.Next.Metadata;
using Il2CppInspector.Reflection;
using static Il2CppInspector.MoraxReader;

namespace Il2CppInspector.Plugin.ABC;

internal sealed partial class AbcMetadata
{
    private readonly ulong[] _genericClasses;
    private readonly Dictionary<ulong, int> _genericInstances;

    private void ReadBinary(ulong[] instancePointers)
    {
        Binary.Metadata = Metadata;
        Image.Version = MetadataVersions.V290;
        // Initialize 0x1807D0BF0: stock pointer arrays, with no registration objects remaining.
        var pointers = Image.ReadMappedUWordArray(Image.ImageBase + 0x157CDB0, 116105);
        Binary.TypeReferenceIndicesByAddress = pointers.Select((p, i) => (p, i)).ToDictionary(p => p.p, p => p.i);
        Binary.GenericInstances =
        [
            .. instancePointers.Select(p => new Il2CppGenericInst { TypeArgc = Image.ReadMappedUInt32(p), TypeArgv = Image.ReadMappedUInt64(p + 8) }),
        ];
        var classIndices = new Dictionary<ulong, int>();
        for (var i = 0; i < _genericClasses.Length; i++)
            classIndices.TryAdd(_genericClasses[i], i);
        var types = new Il2CppType[pointers.Length];
        for (var i = 0; i < types.Length; i++)
        {
            var d = Image.ReadMappedBytes(pointers[i], 16);
            var value = U32(d, 8);
            if ((value & 0x5F000000) != 0 || (d[12] & ~1) != 0)
                throw new InvalidDataException($"ABC type reference {i} contains unverified modifier flags.");
            var data = U64(d, 0);
            if ((Il2CppTypeEnum)(byte)(value >> 16) == Il2CppTypeEnum.IL2CPP_TYPE_GENERICINST)
                data = (ulong)classIndices[data];
            types[i] = new()
            {
                Data = new() { Value = data },
                Value = value,
            };
        }
        Binary.TypeReferences = ImmutableCollectionsMarshal.AsImmutableArray(types);
        ValidateMetadata(pointers);
        // GetGenericMethodFromIndex 0x1807D12A0: method instance, class instance, method definition.
        var specs = Image.ReadMappedBytes(Image.ImageBase + 0xA40610, 279786 * 12);
        Binary.MethodSpecs = Records(
            specs,
            0,
            279786,
            12,
            static (d, _) =>
                new Il2CppMethodSpec
                {
                    MethodIndexIndex = unchecked(I32(d, 0) - 0x3F12D2B9),
                    ClassIndexIndex = unchecked(I32(d, 4) - 0x72A588A3),
                    MethodDefinitionIndex = I32(d, 8) ^ 0x62EAA8A1,
                }
        );
        for (var i = 0; i < 0x7EE0; i++)
        {
            var type = types[I32(_data, 0x4173C8 + i * 4)];
            if (type.Type == Il2CppTypeEnum.IL2CPP_TYPE_GENERICINST)
                GenericLayoutGroups.TryAdd(checked((int)type.Data.Value), I32(_data, 0x59C914 + i * 4));
        }
        var fieldOffsets = new uint[Metadata.Fields.Length];
        var sizes = new Il2CppTypeDefinitionSizes[Metadata.Types.Length];
        for (var i = 0; i < sizes.Length; i++)
        {
            var group = DefinitionLayoutGroup(i);
            sizes[i] = LayoutSizes(group);
            var type = Metadata.Types[i];
            for (var f = 0; f < type.FieldCount; f++)
                fieldOffsets[type.FieldIndex + f] = LayoutFieldOffset(group, f);
        }
        Binary.TypeDefinitionSizes = ImmutableCollectionsMarshal.AsImmutableArray(sizes);
        Binary.FieldOffsets = ImmutableCollectionsMarshal.AsImmutableArray(fieldOffsets);
        Binary.CodeRegistration = new();
        Binary.MetadataRegistration = new() { TypesCount = types.Length, MethodSpecsCount = Binary.MethodSpecs.Length };
        Binary.Modules = [];
        Binary.CustomAttributeGenerators = [];
        Binary.MethodInvokePointers = Image.ReadMappedUWordArray(Image.ImageBase + 0x125E5D0, 0x8B2A);
        var methods = Image.ReadMappedUWordArray(Image.ImageBase + 0x12A3F20, Metadata.Methods.Length);
        var invokers = Metadata.Methods.Select(m => m.InvokerIndex).ToImmutableArray();
        // The fork uses one global method table. Share its normalized module across assembly names.
        var module = new Il2CppCodeGenModule { MethodPointerCount = (uint)methods.Length };
        Binary.ModuleMethodPointers.Add(module, methods);
        Binary.MethodInvokerIndices.Add(module, invokers);
        foreach (var image in Metadata.Images)
        {
            var name = Metadata.Strings[image.NameIndex];
            Binary.Modules.Add(name, module);
        }
        // InitializeGenericMethodTable 0x1807D1A40; inflated method pointers 0x180782A90.
        var table = Image.ReadMappedBytes(Image.ImageBase + 0xEE8260, 0x376370);
        for (var i = 0; i < table.Length; i += 16)
        {
            var spec = Binary.MethodSpecs[I32(table, i)];
            var pointer = unchecked(U32(table, i + 4) - 0x77DFADDA);
            var invoker = unchecked(I32(table, i + 12) - 0x08E050BB);
            if (pointer > 0x2E828 || invoker is < 0 or > 0x8B29)
                throw new InvalidDataException($"ABC generic method table record {i / 16} has invalid pointer or invoker indices.");
            Binary.GenericMethodPointers.TryAdd(spec, Image.ReadMappedUInt64(Image.ImageBase + 0xD74110 + pointer * 8UL));
            Binary.GenericMethodInvokerIndices.TryAdd(spec, invoker);
        }
        ReadUsages();
    }

    internal override int DefinitionLayoutGroup(int definition) => I32(_data, 0x3E65A0 + definition * 4);

    public override Il2CppTypeDefinitionSizes LayoutSizes(int group)
    {
        // Class setup 0x1807D03A0: u16 instance/static/thread sizes, u8 alignment and flags, u32 field start.
        var o = checked(0x1654A08 + group * 12);
        if ((uint)group >= 10198)
            throw new InvalidDataException($"ABC layout group {group} exceeds the layout descriptor table.");
        return new()
        {
            InstanceSize = U16(_data, o),
            StaticFieldsSize = U16(_data, o + 2),
            ThreadStaticFieldsSize = U16(_data, o + 4),
            NativeSize = Image.ReadMappedUInt16(Image.ImageBase + 0x14551C0 + (ulong)group * 2) is var n && n != ushort.MaxValue ? n : -1,
        };
    }

    internal override int LayoutAlignment(int group)
    {
        var alignment = _data[checked(0x1654A08 + group * 12 + 6)];
        return alignment is 1 or 2 or 4 or 8 or 16
            ? alignment
            : throw new InvalidDataException($"ABC layout group {group} has invalid alignment {alignment}.");
    }

    private uint RawFieldOffset(int group, int local) => U32(_data, checked(0x17981F8 + (I32(_data, 0x1654A10 + group * 12) + local) * 4));

    public override uint LayoutFieldOffset(int group, int local)
    {
        var raw = RawFieldOffset(group, local);
        return (raw & 0x01000000) != 0 ? (raw & 0xFFFFFF) | 0x80000000 : raw & 0xFFFFFF;
    }

    public override int FieldStorageTag(Reflection.FieldInfo field)
    {
        var type = field.DeclaringType;
        var definition = type.Definition.IsValid ? type.Definition : type.GetGenericTypeDefinition().Definition;
        return (int)(RawFieldOffset(type.GameLayoutGroup ?? DefinitionLayoutGroup(type.Index), field.Index - (int)definition.FieldIndex) >> 24);
    }

    public override string StorageBase(uint tag) =>
        tag switch
        {
            0 => "klass->static_fields",
            1 => "thread static storage",
            2 => "global static storage",
            4 => "global static storage slot",
            _ => "unknown",
        };

    public override Reflection.TypeInfo ResolveGenericType(TypeModel model, Il2CppType type)
    {
        var record = _genericClasses[checked((int)type.Data.Value)];
        var definitionType = Image.ReadMappedUInt64(record);
        if (definitionType == 0)
            return null;
        var definition = checked((int)Image.ReadMappedUInt64(definitionType));
        var arguments = model.ResolveGenericArguments(Binary.GenericInstances[_genericInstances[Image.ReadMappedUInt64(record + 8)]]);
        var result = model.TypesByDefinitionIndex[definition].MakeGenericType(arguments);
        if (!result.IsEnum && GenericLayoutGroups.TryGetValue(checked((int)type.Data.Value), out var group))
            result.SetGameLayoutGroup(group);
        return result;
    }
}
