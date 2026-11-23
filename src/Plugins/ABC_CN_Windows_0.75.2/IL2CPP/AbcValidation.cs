namespace Il2CppInspector.Plugin.ABC;

internal sealed partial class AbcMetadata
{
    private void ValidateMetadata(ulong[] pointers)
    {
        var types = Binary.TypeReferences;
        var nextType = 0;
        foreach (var image in Metadata.Images)
        {
            if (image.TypeStart != nextType || (uint)image.AssemblyIndex >= Metadata.Assemblies.Length)
                throw new InvalidDataException("ABC image definitions have invalid assembly indices or noncontiguous type ranges.");
            nextType = checked(nextType + (int)image.TypeCount);
        }
        if (nextType != Metadata.Types.Length)
            throw new InvalidDataException("ABC image type ranges do not cover the type definition table.");
        foreach (var type in Metadata.Types)
        {
            Range("type fields", type.FieldIndex, type.FieldCount, Metadata.Fields.Length);
            Range("type methods", type.MethodIndex, type.MethodCount, Metadata.Methods.Length);
            Range("type properties", type.PropertyIndex, type.PropertyCount, Metadata.Properties.Length);
            Range("type events", type.EventIndex, type.EventCount, Metadata.Events.Length);
            Range("type interfaces", type.InterfacesIndex, type.InterfacesCount, Metadata.InterfaceUsageIndices.Length);
            Range("type interface offsets", type.InterfaceOffsetsStart, type.InterfaceOffsetsCount, Metadata.InterfaceOffsets.Length);
            Range("type nested types", type.NestedTypeIndex, type.NestedTypeCount, Metadata.NestedTypeIndices.Length);
            Range("type vtable", type.VTableIndex, type.VTableCount, Metadata.VTableMethodIndices.Length);
            Range("type by-value reference", type.ByValTypeIndex, 1, types.Length);
            Optional("type parent", type.ParentIndex, types.Length);
            Optional("type declaring type", type.DeclaringTypeIndex, types.Length);
            Optional("type element", type.ElementTypeIndex, types.Length);
            Optional("type generic container", type.GenericContainerIndex, Metadata.GenericContainers.Length);
            var native = types[type.ByValTypeIndex];
            var enumType = Image.ReadMappedBytes(pointers[type.ByValTypeIndex] + 12, 1)[0];
            if (type.Bitfield.ValueType != ((native.Value & 0x80000000) != 0) || type.Bitfield.EnumType != (enumType != 0))
                throw new InvalidDataException("ABC type definition flags disagree with their native type references.");
        }
        foreach (var method in Metadata.Methods)
        {
            Range("method declaring type", method.DeclaringType, 1, Metadata.Types.Length);
            Range("method return type", method.ReturnType, 1, types.Length);
            Range("method parameters", method.ParameterStart, method.ParameterCount, Metadata.Params.Length);
            Optional("method generic container", method.GenericContainerIndex, Metadata.GenericContainers.Length);
        }
        foreach (var field in Metadata.Fields)
            Range("field type", field.TypeIndex, 1, types.Length);
        foreach (var parameter in Metadata.Params)
            Range("parameter type", parameter.TypeIndex, 1, types.Length);
        foreach (var container in Metadata.GenericContainers)
            Range("generic parameters", container.GenericParameterStart, container.TypeArgc, Metadata.GenericParameters.Length);
        foreach (var parameter in Metadata.GenericParameters)
        {
            Range("generic parameter owner", parameter.OwnerIndex, 1, Metadata.GenericContainers.Length);
            Range("generic parameter constraints", parameter.ConstraintsStart, parameter.ConstraintsCount, Metadata.GenericConstraintIndices.Length);
        }
        foreach (var index in Metadata.NestedTypeIndices)
            Range("nested type", index, 1, Metadata.Types.Length);
        foreach (var index in Metadata.InterfaceUsageIndices.Concat(Metadata.GenericConstraintIndices))
            Range("interface or constraint type", index, 1, types.Length);
        foreach (var pair in Metadata.InterfaceOffsets)
            Range("interface offset type", pair.InterfaceTypeIndex, 1, types.Length);
    }

    private static void Optional(string name, int index, int total)
    {
        if (index != -1)
            Range(name, index, 1, total);
    }

    private static void Range(string name, int start, int count, int total)
    {
        if (count < 0 || (count != 0 && (start < 0 || start > total - count)))
            throw new InvalidDataException($"ABC {name} range ({start}, {count}) exceeds its table of {total} records.");
    }
}
