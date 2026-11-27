/*
    Copyright (c) 2019-2020 Carter Bush - https://github.com/carterbush
    Copyright (c) 2020-2021 Katy Coe - http://www.djkaty.com - https://github.com/djkaty
    Copyright 2020 Robert Xiao - https://robertxiao.ca

    All rights reserved.
*/

using Il2CppInspector.Next;
using Il2CppInspector.Next.Metadata;

namespace Il2CppInspector
{
    public enum MetadataUsageType
    {
        TypeInfo = 1,
        Type = 2,
        MethodDef = 3,
        FieldInfo = 4,
        StringLiteral = 5,
        MethodRef = 6,
        FieldRva = 7,
    }

    public record struct MetadataUsage
    {
        public MetadataUsageType Type { get; }
        public int SourceIndex { get; }
        public ulong VirtualAddress { get; }

        public readonly bool IsValid => Type != 0;

        public MetadataUsage(MetadataUsageType type, int sourceIndex, ulong virtualAddress = 0)
        {
            Type = type;
            SourceIndex = sourceIndex;
            VirtualAddress = virtualAddress;
        }

        public static MetadataUsage FromEncodedIndex(Il2CppInspector package, uint encodedIndex, ulong virtualAddress = 0)
        {
            uint index;
            MetadataUsageType usageType;
            if (package.Version < MetadataVersions.V180)
            {
                /* These encoded indices appear only in vtables, and are decoded by IsGenericMethodIndex/GetDecodedMethodIndex */
                var isGeneric = encodedIndex & 0x80000000;
                index = package.Binary.VTableMethodReferences[(int)(encodedIndex & 0x7FFFFFFF)];
                usageType = (isGeneric != 0) ? MetadataUsageType.MethodRef : MetadataUsageType.MethodDef;
            }
            else
            {
                var decoded = Il2CppMetadataUsage.FromValue(package.Version, encodedIndex);
                usageType = (MetadataUsageType)decoded.Type;
                index = decoded.Index;
            }
            return new MetadataUsage(usageType, (int)index, virtualAddress);
        }
    }
}
