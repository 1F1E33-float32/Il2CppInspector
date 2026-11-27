using Il2CppInspector.Next;
using Il2CppInspector.Next.BinaryMetadata;
using VersionedSerialization;

namespace Il2CppInspector;

public abstract partial class Il2CppBinary
{
    private bool _validatingMetadataVersion;

    internal static Il2CppBinary LoadForMetadataVersionDetection(IFileFormatStream image, Metadata metadata)
    {
        foreach (var strategy in image.TryNextLoadStrategy())
        {
            var binary = LoadImpl(strategy, null);
            binary._validatingMetadataVersion = true;
            if (binary.FindRegistrationStructs(metadata))
                return binary;
        }
        return null;
    }

    // Trial versions must not allocate tables from unchecked counts or pointers in a mismatched registration layout.
    private void ValidateRegistrationForVersionDetection()
    {
        var config = new ReaderConfig(Image.Bits == 32);
        var pointerSize = Image.Bits / 8;
        var registration = MetadataRegistration;
        var code = CodeRegistration;
        if (
            registration.TypeDefinitionsSizesCount != Metadata.Types.Length
            || registration.TypesCount <= 0
            || (Image.Version >= MetadataVersions.V220 && registration.FieldOffsetsCount != Metadata.Types.Length)
        )
            throw new InvalidDataException("Metadata version candidate disagrees with the registered type count.");
        ValidatePointerTable(registration.Types, registration.TypesCount, Il2CppType.Size(Image.Version, config));
        ValidatePointerTable(registration.TypeDefinitionsSizes, registration.TypeDefinitionsSizesCount, Il2CppTypeDefinitionSizes.StructSize());
        ValidatePointerTable(registration.GenericInsts, registration.GenericInstsCount, Il2CppGenericInst.StructSize(Image.Version, config));
        // GenericClasses is not dereferenced while loading; unused entries can be null.
        ValidateTable(registration.GenericClasses, registration.GenericClassesCount, pointerSize);
        ValidateTable(registration.FieldOffsets, registration.FieldOffsetsCount, Image.Version >= MetadataVersions.V220 ? pointerSize : 4);
        ValidateTable(code.GenericMethodPointers, code.GenericMethodPointersCount, pointerSize);
        ValidateTable(code.InvokerPointers, code.InvokerPointersCount, pointerSize);
        if (Image.Version < MetadataVersions.V1080)
        {
            ValidateTable(registration.MethodSpecs, registration.MethodSpecsCount, Il2CppMethodSpec.StructSize(Image.Version, config));
            ValidateTable(
                registration.GenericMethodTable,
                registration.GenericMethodTableCount,
                Il2CppGenericMethodFunctionsDefinitions.StructSize(Image.Version, config)
            );
        }
        if (Image.Version < MetadataVersions.V270)
            ValidateTable(code.CustomAttributeGenerators, code.CustomAttributeCount, pointerSize);
        if (Image.Version < MetadataVersions.V180)
            ValidateTable(registration.MethodReferences, registration.MethodReferencesCount, 4);
        if (Image.Version <= MetadataVersions.V241)
            ValidateTable(code.MethodPointers, code.MethodPointersCount, pointerSize);
        else
        {
            if (code.CodeGenModulesCount != Metadata.Images.Length)
                throw new InvalidDataException("Metadata version candidate disagrees with the registered module count.");
            ValidatePointerTable(code.CodeGenModules, code.CodeGenModulesCount, Il2CppCodeGenModule.StructSize(Image.Version, config));
            for (var i = 0; i < code.CodeGenModulesCount; i++)
            {
                var address = Image.ReadMappedUWord(code.CodeGenModules + (ulong)i * (ulong)pointerSize);
                var module = Image.ReadMappedVersionedObject<Il2CppCodeGenModule>(address);
                // Abstract/stripped methods can leave the pointer array in BSS, but its count is still bounded by MethodDef RIDs.
                if (module.MethodPointerCount > 0xFFFFFF || module.MethodPointerCount > Image.Length / pointerSize)
                    throw new InvalidDataException("Metadata version candidate has an invalid module method count.");
                if (module.MethodPointers != 0 && Image.TryMapVATR(module.MethodPointers, out _))
                    ValidateTable(module.MethodPointers, module.MethodPointerCount, pointerSize);
                if (Image.Version < MetadataVersions.V1080 && module.InvokerIndices != 0 && Image.TryMapVATR(module.InvokerIndices, out _))
                    ValidateTable(module.InvokerIndices, module.MethodPointerCount, 4);
            }
        }

        void ValidateTable(ulong address, long count, int elementSize)
        {
            if (count < 0 || count > int.MaxValue || count > Image.Length / elementSize)
                throw new InvalidDataException("Metadata version candidate has an invalid native table count.");
            if (count == 0)
                return;
            var length = count * elementSize;
            if (
                address == 0
                || !Image.TryMapVATR(address, out var start)
                || length > Image.Length - start
                || address > ulong.MaxValue - (ulong)(length - 1)
                || !Image.TryMapVATR(address + (ulong)length - 1, out var end)
                || end != (long)start + length - 1
            )
                throw new InvalidDataException("Metadata version candidate has a native table outside the mapped binary.");
        }

        void ValidatePointerTable(ulong address, long count, int elementSize)
        {
            ValidateTable(address, count, pointerSize);
            for (var i = 0L; i < count; i++)
            {
                if ((i & 0xFFF) == 0)
                    AnalysisLog.CheckCancellation();
                var pointer = Image.ReadMappedUWord(address + (ulong)(i * pointerSize));
                ValidateTable(pointer, 1, elementSize);
            }
        }
    }
}
