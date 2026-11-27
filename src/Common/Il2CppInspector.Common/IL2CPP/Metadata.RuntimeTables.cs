using System.Collections.Immutable;
using Il2CppInspector.Next.BinaryMetadata;
using Il2CppInspector.Next.Metadata;

namespace Il2CppInspector;

public partial class Metadata
{
    // GlobalMetadata::BuildGenericMethodFromMethodSpec concatenates three category tables in this order in Unity 6000.6.
    internal ImmutableArray<Il2CppMethodSpec> ReadMethodSpecs(int genericInstanceCount)
    {
        var methodWidth = MethodIndex.Size(Version);
        var instanceWidth = GetIndexSize(genericInstanceCount);
        var total = (long)Header.MethodSpecsOnGenericType.Count + Header.GenericMethodSpecsOnType.Count + Header.MethodSpecs.Count;
        if (total < 0 || total > int.MaxValue)
            throw new InvalidDataException("Metadata method specification count exceeds the supported range.");
        var result = ImmutableArray.CreateBuilder<Il2CppMethodSpec>((int)total);
        foreach (
            var (section, classInstance, methodInstance, name) in new[]
            {
                (Header.MethodSpecsOnGenericType, true, false, "methodSpecsOnGenericType"),
                (Header.GenericMethodSpecsOnType, false, true, "genericMethodSpecsOnType"),
                (Header.MethodSpecs, true, true, "methodSpecs"),
            }
        )
        {
            ValidateSection(section, methodWidth + instanceWidth * (classInstance && methodInstance ? 2 : 1), name);
            Position = section.Offset;
            for (var i = 0; i < section.Count; i++)
            {
                var method = ReadCompactIndex(methodWidth);
                var klass = classInstance ? ReadCompactIndex(instanceWidth) : -1;
                var instance = methodInstance ? ReadCompactIndex(instanceWidth) : -1;
                if (
                    (uint)method >= Methods.Length
                    || klass < -1
                    || klass >= genericInstanceCount
                    || instance < -1
                    || instance >= genericInstanceCount
                )
                    throw new InvalidDataException($"Metadata {name} record {i} contains an out-of-range method or generic instance index.");
                result.Add(
                    new Il2CppMethodSpec
                    {
                        MethodDefinitionIndex = method,
                        ClassIndexIndex = klass,
                        MethodIndexIndex = instance,
                    }
                );
            }
        }
        return result.MoveToImmutable();
    }

    internal ImmutableArray<Il2CppGenericMethodFunctionsDefinitions> ReadGenericMethodTable(
        int specificationCount,
        uint methodPointerCount,
        uint invokerCount
    )
    {
        var specificationWidth = GetIndexSize(specificationCount);
        var pointerWidth = GetIndexSize(methodPointerCount);
        var invokerWidth = GetIndexSize(invokerCount);
        var adjustorWidth = GetIndexSize(Header.GenericMethodFunctionsDefinitionsWithAdjustor.Count);
        var total = (long)Header.GenericMethodFunctionsDefinitions.Count + Header.GenericMethodFunctionsDefinitionsWithAdjustor.Count;
        if (total < 0 || total > int.MaxValue)
            throw new InvalidDataException("Metadata generic method table count exceeds the supported range.");
        var result = ImmutableArray.CreateBuilder<Il2CppGenericMethodFunctionsDefinitions>((int)total);
        foreach (
            var (section, hasAdjustor, name) in new[]
            {
                (Header.GenericMethodFunctionsDefinitions, false, "genericMethodFunctionsDefinitions"),
                (Header.GenericMethodFunctionsDefinitionsWithAdjustor, true, "genericMethodFunctionsDefinitionsWithAdjustor"),
            }
        )
        {
            ValidateSection(section, specificationWidth + pointerWidth + invokerWidth + (hasAdjustor ? adjustorWidth : 0), name);
            Position = section.Offset;
            for (var i = 0; i < section.Count; i++)
            {
                var specification = ReadCompactIndex(specificationWidth);
                var pointer = ReadCompactIndex(pointerWidth);
                var invoker = ReadCompactIndex(invokerWidth);
                var adjustor = hasAdjustor ? ReadCompactIndex(adjustorWidth) : -1;
                if (
                    (uint)specification >= specificationCount
                    || pointer < -1
                    || (long)pointer >= methodPointerCount
                    || invoker < -1
                    || (long)invoker >= invokerCount
                    || adjustor < -1
                    || adjustor >= Header.GenericMethodFunctionsDefinitionsWithAdjustor.Count
                )
                    throw new InvalidDataException($"Metadata {name} record {i} contains an out-of-range function index.");
                result.Add(
                    new Il2CppGenericMethodFunctionsDefinitions
                    {
                        GenericMethodIndex = specification,
                        Indices = new Il2CppGenericMethodIndices
                        {
                            MethodIndex = pointer,
                            InvokerIndex = invoker,
                            AdjustorThunkIndex = adjustor,
                        },
                    }
                );
            }
        }
        return result.MoveToImmutable();
    }

    internal ImmutableArray<int> ReadInvokerIndices(Il2CppImageDefinition image, uint methodCount, uint invokerCount)
    {
        var section = Header.InvokerIndices;
        var width = GetIndexSize(invokerCount);
        ValidateSection(section, width, "invokerIndices");
        if (methodCount > int.MaxValue || (section.Count != 0 && (long)image.InvokerIndicesStart + methodCount > section.Count))
            throw new InvalidDataException("Metadata image invoker range exceeds the invokerIndices section.");
        var result = ImmutableArray.CreateBuilder<int>((int)methodCount);
        Position = section.Offset + (long)image.InvokerIndicesStart * width;
        for (var i = 0; i < methodCount; i++)
        {
            var index = section.Count == 0 ? -1 : ReadCompactIndex(width);
            if (index < -1 || (long)index >= invokerCount)
                throw new InvalidDataException($"Metadata invokerIndices record {image.InvokerIndicesStart + i} exceeds the invoker pointer table.");
            result.Add(index);
        }
        return result.MoveToImmutable();
    }

    private static int GetIndexSize(long elementCount) =>
        elementCount switch
        {
            < 0 => throw new InvalidDataException("Metadata index section has a negative element count."),
            <= byte.MaxValue => sizeof(byte),
            <= ushort.MaxValue => sizeof(ushort),
            <= int.MaxValue => sizeof(int),
            _ => throw new InvalidDataException("Metadata index section exceeds the supported element count."),
        };

    private int ReadCompactIndex(int width)
    {
        return width switch
        {
            1 => ReadByte() is var value && value != byte.MaxValue ? value : -1,
            2 => ReadUInt16() is var value && value != ushort.MaxValue ? value : -1,
            4 => ReadInt32(),
            _ => throw new InvalidDataException($"Unsupported metadata index width {width}; expected 1, 2 or 4 bytes."),
        };
    }

    private void ValidateSection(Il2CppSectionMetadata section, int recordSize, string name)
    {
        if (
            section.Count < 0
            || section.SectionSize < 0
            || section.Offset < 0
            || (long)section.Count * recordSize != section.SectionSize
            || (long)section.Offset + section.SectionSize > Length
        )
            throw new InvalidDataException(
                $"Metadata section {name} has an invalid offset, size or count; expected {recordSize} bytes per record within the file."
            );
    }
}
