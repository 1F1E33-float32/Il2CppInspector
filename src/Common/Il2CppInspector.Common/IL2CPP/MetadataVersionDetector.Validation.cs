using Il2CppInspector.Next;
using Il2CppInspector.Next.BinaryMetadata;
using Il2CppInspector.Next.Metadata;
using VersionedSerialization;

namespace Il2CppInspector;

internal static partial class MetadataVersionDetector
{
    private static bool ValidateMetadata(Metadata metadata)
    {
        var h = metadata.Header;
        if (metadata.Images.IsEmpty || metadata.Types.IsEmpty || metadata.Assemblies.Length != metadata.Images.Length)
            return false;

        // Old headers store byte lengths. Stock loading tolerates some subversion probes with partial records;
        // a final detection candidate must instead consume every record exactly.
        bool Records<T>(int bytes, Il2CppSectionMetadata section)
            where T : IReadable, new()
        {
            // Metadata.Initialize reads the Unity 2018 backported assembly records with the 24.4 layout.
            var recordVersion =
                typeof(T) == typeof(Il2CppAssemblyDefinition) && metadata.Version.HasTag(MetadataVersions.Tag2018)
                    ? MetadataVersions.V244
                    : metadata.Version;
            var size = T.Size(recordVersion, default);
            return metadata.Version >= MetadataVersions.V390 ? (long)section.Count * size == section.SectionSize : bytes % size == 0;
        }
        if (
            !Records<Il2CppImageDefinition>(h.ImagesSize, h.Images)
            || !Records<Il2CppAssemblyDefinition>(h.AssembliesSize, h.Assemblies)
            || !Records<Il2CppTypeDefinition>(h.TypeDefinitionsSize, h.TypeDefinitions)
            || !Records<Il2CppMethodDefinition>(h.MethodsSize, h.Methods)
            || !Records<Il2CppParameterDefinition>(h.ParametersSize, h.Parameters)
            || !Records<Il2CppFieldDefinition>(h.FieldsSize, h.Fields)
            || !Records<Il2CppPropertyDefinition>(h.PropertiesSize, h.Properties)
            || !Records<Il2CppEventDefinition>(h.EventsSize, h.Events)
            || !Records<Il2CppGenericContainer>(h.GenericContainersSize, h.GenericContainers)
            || !Records<Il2CppGenericParameter>(h.GenericParametersSize, h.GenericParameters)
            || !Records<Il2CppInterfaceOffsetPair>(h.InterfaceOffsetsSize, h.InterfaceOffsets)
            || !Records<Il2CppFieldDefaultValue>(h.FieldDefaultValuesSize, h.FieldDefaultValues)
            || !Records<Il2CppParameterDefaultValue>(h.ParameterDefaultValuesSize, h.ParameterDefaultValues)
        )
            return false;

        bool Name(int index) => metadata.Strings.ContainsKey(index);
        var seenTypes = new bool[metadata.Types.Length];
        for (var i = 0; i < metadata.Images.Length; i++)
        {
            var image = metadata.Images[i];
            // Synthetic modules can have a nil Module token (0); normal modules use token 1.
            if (
                !Name(image.NameIndex)
                || !Index(image.AssemblyIndex, metadata.Assemblies.Length)
                || metadata.Assemblies[image.AssemblyIndex].ImageIndex != i
                || !Range(image.TypeStart, image.TypeCount, metadata.Types.Length)
                || (metadata.Version >= MetadataVersions.V190 && image.Token > 1)
                || !Index(image.EntryPointIndex, metadata.Methods.Length, optional: true)
            )
                return false;
            for (var t = (int)image.TypeStart; t < image.TypeStart + image.TypeCount; t++)
            {
                if (seenTypes[t])
                    return false;
                seenTypes[t] = true;
            }
        }
        if (seenTypes.Any(static seen => !seen))
            return false;
        foreach (var assembly in metadata.Assemblies)
            if (!Name(assembly.Aname.NameIndex) || !Name(assembly.Aname.CultureIndex))
                return false;

        var seenMethods = new bool[metadata.Methods.Length];
        for (var t = 0; t < metadata.Types.Length; t++)
        {
            if ((t & 0xFFF) == 0)
                AnalysisLog.CheckCancellation();
            var type = metadata.Types[t];
            if (
                !Name(type.NameIndex)
                || !Name(type.NamespaceIndex)
                || (metadata.Version >= MetadataVersions.V190 && !Token(type.Token, 0x02))
                || !Range(type.MethodIndex, type.MethodCount, metadata.Methods.Length)
                || !Range(type.FieldIndex, type.FieldCount, metadata.Fields.Length)
                || !Range(type.PropertyIndex, type.PropertyCount, metadata.Properties.Length)
                || !Range(type.EventIndex, type.EventCount, metadata.Events.Length)
                || !Range(type.InterfacesIndex, type.InterfacesCount, metadata.InterfaceUsageIndices.Length)
                || !Range(type.InterfaceOffsetsStart, type.InterfaceOffsetsCount, metadata.InterfaceOffsets.Length)
                || !Range(type.NestedTypeIndex, type.NestedTypeCount, metadata.NestedTypeIndices.Length)
                || !Range(type.VTableIndex, type.VTableCount, metadata.VTableMethodIndices.Length)
                || !Index(type.GenericContainerIndex, metadata.GenericContainers.Length, optional: true)
            )
                return false;
            for (var m = (int)type.MethodIndex; m < type.MethodIndex + type.MethodCount; m++)
            {
                if (seenMethods[m] || metadata.Methods[m].DeclaringType != t)
                    return false;
                seenMethods[m] = true;
            }
            for (var p = (int)type.PropertyIndex; p < type.PropertyIndex + type.PropertyCount; p++)
                if (!Index(metadata.Properties[p].Get, type.MethodCount, true) || !Index(metadata.Properties[p].Set, type.MethodCount, true))
                    return false;
            for (var e = (int)type.EventIndex; e < type.EventIndex + type.EventCount; e++)
                if (
                    !Index(metadata.Events[e].Add, type.MethodCount, true)
                    || !Index(metadata.Events[e].Remove, type.MethodCount, true)
                    || !Index(metadata.Events[e].Raise, type.MethodCount, true)
                )
                    return false;
        }
        if (seenMethods.Any(static seen => !seen))
            return false;
        foreach (var method in metadata.Methods)
            if (
                !Name(method.NameIndex)
                || !Token(method.Token, 0x06)
                || !Range(method.ParameterStart, method.ParameterCount, metadata.Params.Length)
                || !Index(method.GenericContainerIndex, metadata.GenericContainers.Length, true)
            )
                return false;
        // Parameters without Param rows retain a nil ParamDef RID.
        foreach (var parameter in metadata.Params)
            if (!Name(parameter.NameIndex) || parameter.Token >> 24 != 0x08)
                return false;
        foreach (var field in metadata.Fields)
            if (!Name(field.NameIndex) || (metadata.Version >= MetadataVersions.V190 && !Token(field.Token, 0x04)))
                return false;
        foreach (var property in metadata.Properties)
            if (!Name(property.NameIndex) || (metadata.Version >= MetadataVersions.V190 && !Token(property.Token, 0x17)))
                return false;
        foreach (var evt in metadata.Events)
            if (!Name(evt.NameIndex) || (metadata.Version >= MetadataVersions.V190 && !Token(evt.Token, 0x14)))
                return false;
        foreach (var generic in metadata.GenericContainers)
            if (
                generic.IsMethod is not (0 or 1)
                || !Index(generic.OwnerIndex, generic.IsMethod == 0 ? metadata.Types.Length : metadata.Methods.Length)
                || !Range(generic.GenericParameterStart, generic.TypeArgc, metadata.GenericParameters.Length)
            )
                return false;
        foreach (var parameter in metadata.GenericParameters)
            if (
                !Name(parameter.NameIndex)
                || !Index(parameter.OwnerIndex, metadata.GenericContainers.Length)
                || !Range(parameter.ConstraintsStart, parameter.ConstraintsCount, metadata.GenericConstraintIndices.Length)
            )
                return false;
        return metadata.NestedTypeIndices.All(index => Index(index, metadata.Types.Length));
    }

    private static bool ValidateBinary(Metadata metadata, Il2CppBinary binary)
    {
        var count = binary.TypeReferences.Length;
        foreach (var type in metadata.Types)
            if (
                !Index(type.ByValTypeIndex, count)
                || !Index(type.ParentIndex, count, true)
                || !Index(type.DeclaringTypeIndex, count, true)
                || (metadata.Version <= MetadataVersions.V245 && !Index(type.ByRefTypeIndex, count))
            )
                return false;
        foreach (var method in metadata.Methods)
            if (!Index(method.ReturnType, count))
                return false;
        foreach (var parameter in metadata.Params)
            if (!Index(parameter.TypeIndex, count))
                return false;
        foreach (var field in metadata.Fields)
            if (!Index(field.TypeIndex, count))
                return false;
        foreach (var evt in metadata.Events)
            if (!Index(evt.TypeIndex, count))
                return false;
        var defaultBytes =
            metadata.Version >= MetadataVersions.V390
                ? metadata.Header.FieldAndParameterDefaultValueData.SectionSize
                : metadata.Header.FieldAndParameterDefaultValueDataSize;
        foreach (var value in metadata.FieldDefaultValues)
            if (!Index(value.FieldIndex, metadata.Fields.Length) || !Index(value.TypeIndex, count) || !Index(value.DataIndex, defaultBytes, true))
                return false;
        foreach (var value in metadata.ParameterDefaultValues)
            if (!Index(value.ParameterIndex, metadata.Params.Length) || !Index(value.TypeIndex, count) || !Index(value.DataIndex, defaultBytes, true))
                return false;
        foreach (var offset in metadata.InterfaceOffsets)
            if (!Index(offset.InterfaceTypeIndex, count))
                return false;
        if (metadata.InterfaceUsageIndices.Any(index => !Index(index, count)) || metadata.GenericConstraintIndices.Any(index => !Index(index, count)))
            return false;
        foreach (var type in binary.TypeReferences)
            if (
                (type.Type.IsTypeDefinitionEnum() && !Index(type.Data.KlassIndex, metadata.Types.Length))
                || (type.Type.IsGenericParameterEnum() && !Index(type.Data.GenericParameterIndex, metadata.GenericParameters.Length))
            )
                return false;
        foreach (var spec in binary.MethodSpecs)
            if (
                !Index(spec.MethodDefinitionIndex, metadata.Methods.Length)
                || !Index(spec.ClassIndexIndex, binary.GenericInstances.Length, true)
                || !Index(spec.MethodIndexIndex, binary.GenericInstances.Length, true)
            )
                return false;
        if (binary.Image.Version >= MetadataVersions.V242)
        {
            foreach (var image in metadata.Images)
            {
                if (!binary.Modules.TryGetValue(metadata.Strings[image.NameIndex], out var module))
                    return false;
                foreach (var index in binary.MethodInvokerIndices[module])
                    if (!Index(index, binary.MethodInvokePointers.Length, true))
                        return false;
                for (var t = (int)image.TypeStart; t < image.TypeStart + image.TypeCount; t++)
                {
                    var type = metadata.Types[t];
                    for (var m = (int)type.MethodIndex; m < type.MethodIndex + type.MethodCount; m++)
                        if ((metadata.Methods[m].Token & 0xFFFFFF) > module.MethodPointerCount)
                            return false;
                }
            }
        }
        else
        {
            foreach (var method in metadata.Methods)
                if (
                    !Index(method.MethodIndex, binary.GlobalMethodPointers.Length, true)
                    || !Index(method.InvokerIndex, binary.MethodInvokePointers.Length, true)
                )
                    return false;
        }
        return binary.GenericMethodInvokerIndices.Values.All(index => Index(index, binary.MethodInvokePointers.Length, true));
    }

    private static bool Index(int index, int count, bool optional = false) => (uint)index < (uint)count || (optional && index == -1);

    private static bool Range(int start, long count, int total) => count >= 0 && start >= (count == 0 ? -1 : 0) && (long)start + count <= total;

    private static bool Token(uint token, uint table) => token >> 24 == table && (token & 0xFFFFFF) != 0;
}
