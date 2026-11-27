/*
    Copyright 2017 Perfare - https://github.com/Perfare/Il2CppDumper
    Copyright 2017-2021 Katy Coe - http://www.djkaty.com - https://github.com/djkaty

    All rights reserved.
*/

using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using Il2CppInspector.Next;
using Il2CppInspector.Next.Metadata;
using VersionedSerialization;

namespace Il2CppInspector
{
    public partial class Metadata : BinaryObjectStreamReader
    {
        public Il2CppGlobalMetadataHeader Header { get; set; }

        public ImmutableArray<Il2CppAssemblyDefinition> Assemblies { get; set; }
        public ImmutableArray<Il2CppImageDefinition> Images { get; set; }
        public ImmutableArray<Il2CppTypeDefinition> Types { get; set; }
        public ImmutableArray<Il2CppMethodDefinition> Methods { get; set; }
        public ImmutableArray<Il2CppParameterDefinition> Params { get; set; }
        public ImmutableArray<Il2CppFieldDefinition> Fields { get; set; }
        public ImmutableArray<Il2CppFieldDefaultValue> FieldDefaultValues { get; set; }
        public ImmutableArray<Il2CppParameterDefaultValue> ParameterDefaultValues { get; set; }
        public ImmutableArray<Il2CppPropertyDefinition> Properties { get; set; }
        public ImmutableArray<Il2CppEventDefinition> Events { get; set; }
        public ImmutableArray<Il2CppGenericContainer> GenericContainers { get; set; }
        public ImmutableArray<Il2CppGenericParameter> GenericParameters { get; set; }
        public ImmutableArray<Il2CppCustomAttributeTypeRange> AttributeTypeRanges { get; set; }
        public ImmutableArray<Il2CppCustomAttributeDataRange> AttributeDataRanges { get; set; }
        public ImmutableArray<Il2CppInterfaceOffsetPair> InterfaceOffsets { get; set; }
        public ImmutableArray<Il2CppMetadataUsageList> MetadataUsageLists { get; set; }
        public ImmutableArray<Il2CppMetadataUsagePair> MetadataUsagePairs { get; set; }
        public ImmutableArray<Il2CppFieldRef> FieldRefs { get; set; }

        public ImmutableArray<TypeIndex> InterfaceUsageIndices { get; set; }
        public ImmutableArray<int> NestedTypeIndices { get; set; }
        public ImmutableArray<int> AttributeTypeIndices { get; set; }
        public ImmutableArray<TypeIndex> GenericConstraintIndices { get; set; }
        public ImmutableArray<uint> VTableMethodIndices { get; set; }
        public string[] StringLiterals { get; set; }

        public ImmutableArray<Il2CppInlineArrayLength> TypeInlineArrays { get; set; }

        public int FieldAndParameterDefaultValueDataOffset =>
            Version >= MetadataVersions.V390 ? Header.FieldAndParameterDefaultValueData.Offset : Header.FieldAndParameterDefaultValueDataOffset;

        public int AttributeDataOffset =>
            GameAdapter is { AttributeDataOffset: >= 0 } adapter ? adapter.AttributeDataOffset
            : Version >= MetadataVersions.V390 ? Header.AttributeData.Offset
            : Header.AttributeDataOffset;

        public Dictionary<int, string> Strings { get; private set; } = [];
        public Dictionary<int, byte[]> AssemblyPublicKeys { get; private set; } = [];

        // Set if something in the metadata has been modified / decrypted
        public bool IsModified { get; private set; }

        // Status update callback
        private EventHandler<string> OnStatusUpdate { get; set; }

        private void StatusUpdate(string status) => OnStatusUpdate?.Invoke(this, status);

        // Initialize metadata object from a stream
        public static Metadata FromStream(MemoryStream stream, EventHandler<string> statusCallback = null)
        {
            var metadata = new Metadata(statusCallback);
            stream.Position = 0;
            stream.CopyTo(metadata);
            metadata.Position = 0;
            metadata.Initialize();
            return metadata;
        }

        private Metadata(EventHandler<string> statusCallback = null) => OnStatusUpdate = statusCallback;

        private void Initialize()
        {
            StatusUpdate("Processing metadata");

            // Read metadata header
            Header = ReadVersionedObject<Il2CppGlobalMetadataHeader>(0);

            // Check for correct magic bytes
            if (!Header.SanityValid)
            {
                throw new InvalidDataException("The metadata header has an invalid sanity value; expected 0xFAB11BAF.");
            }

            // Set object versioning for Bin2Object from metadata version
            Version = new StructVersion(Header.Version);

            // Only formats used by final/patch releases are supported.
            if (Header.Version is not (16 or 18 or 19 or 20 or 21 or 22 or 23 or 24 or 27 or 29 or 31 or 39 or 106 or 107 or 108))
            {
                throw new NotSupportedException($"Metadata version {Header.Version} is not supported.");
            }

            // Rewind and read metadata header with the correct version settings
            Header = ReadVersionedObject<Il2CppGlobalMetadataHeader>(0);

            // Setup the proper index sizes for metadata v39+
            if (Version >= MetadataVersions.V390)
            {
                var typeDefinitionIndexSize = GetIndexSize(Header.TypeDefinitions.Count);
                var genericContainerIndexSize = GetIndexSize(Header.GenericContainers.Count);

                var tag = $"{TypeDefinitionIndex.TagPrefix}{typeDefinitionIndexSize}_{GenericContainerIndex.TagPrefix}{genericContainerIndexSize}";

                // MetadataDeserialization reads these sections without padding. Interfaces and constraints
                // contain only TypeIndex values and remain usable when the interface-offset table is empty.
                // This derives the width without needing Il2CppMetadataRegistration::typesCount from the binary.
                var typeIndexSize = 0;
                foreach (
                    var (section, otherBytes) in new[]
                    {
                        (Header.InterfaceOffsets, sizeof(int)),
                        (Header.Interfaces, 0),
                        (Header.GenericParameterConstraints, 0),
                        (Header.Fields, 8),
                    }
                )
                {
                    if (section.Count == 0)
                        continue;
                    if (section.Count < 0 || section.SectionSize < 0 || section.SectionSize % section.Count != 0)
                        throw new InvalidDataException("Metadata TypeIndex section has an invalid size or count.");
                    var size = section.SectionSize / section.Count - otherBytes;
                    if (size is not (1 or 2 or 4) || (typeIndexSize != 0 && typeIndexSize != size))
                        throw new InvalidDataException("Metadata sections disagree on the serialized TypeIndex width; expected 1, 2 or 4 bytes.");
                    typeIndexSize = size;
                }
                if (typeIndexSize == 0)
                    throw new InvalidDataException("Cannot determine the serialized TypeIndex width from empty metadata sections.");

                var fullTag = $"{tag}_{TypeIndex.TagPrefix}{typeIndexSize}";

                var parameterIndexSize = GetIndexSize(Header.Parameters.Count);
                fullTag += $"_{ParameterIndex.TagPrefix}{parameterIndexSize}";

                if (Version >= MetadataVersions.V1060)
                {
                    var eventIndexSize = GetIndexSize(Header.Events.Count);
                    var interfacesIndexSize = GetIndexSize(Header.InterfaceOffsets.Count);
                    var nestedTypeIndexSize = GetIndexSize(Header.NestedTypes.Count);
                    var propertyIndexSize = GetIndexSize(Header.Properties.Count);
                    fullTag +=
                        $"_{EventIndex.TagPrefix}{eventIndexSize}"
                        + $"_{InterfacesIndex.TagPrefix}{interfacesIndexSize}"
                        + $"_{NestedTypeIndex.TagPrefix}{nestedTypeIndexSize}"
                        + $"_{PropertyIndex.TagPrefix}{propertyIndexSize}";
                    var methodIndexSize = GetIndexSize(Header.Methods.Count);
                    fullTag += $"_{MethodIndex.TagPrefix}{methodIndexSize}";
                    var genericParameterIndexSize = GetIndexSize(Header.GenericParameters.Count);
                    var fieldIndexSize = GetIndexSize(Header.Fields.Count);
                    var defaultValueDataIndex = GetIndexSize(Header.FieldAndParameterDefaultValueData.Count);
                    fullTag +=
                        $"_{GenericParameterIndex.TagPrefix}{genericParameterIndexSize}"
                        + $"_{FieldIndex.TagPrefix}{fieldIndexSize}"
                        + $"_{DefaultValueDataIndex.TagPrefix}{defaultValueDataIndex}";
                }

                Version = new StructVersion(Version.Major, Version.Minor, fullTag);
            }

            // Sanity checking
            // Unity.IL2CPP.MetadataCacheWriter.WriteLibIl2CppMetadata always writes the metadata information in the same order it appears in the header,
            // with each block always coming directly after the previous block, 4-byte aligned. We can use this to check the integrity of the data and
            // detect sub-versions.

            // For metadata v24.0, the header can either be either 0x110 (24.0, 24.1) or 0x108 (24.2) bytes long. Since 'stringLiteralOffset' is the first thing
            // in the header after the sanity and version fields, and since it will always point directly to the first byte after the end of the header,
            // we can use this value to determine the actual header length and therefore narrow down the metadata version to 24.0/24.1 or 24.2.

            var realHeaderLength = Version >= MetadataVersions.V390 ? Header.StringLiterals.Offset : Header.StringLiteralOffset;

            if (realHeaderLength != Sizeof<Il2CppGlobalMetadataHeader>())
            {
                if (Version == MetadataVersions.V240)
                {
                    Version = MetadataVersions.V242;
                    Header = ReadVersionedObject<Il2CppGlobalMetadataHeader>(0);
                }
            }

            if (realHeaderLength != Sizeof<Il2CppGlobalMetadataHeader>())
            {
                throw new InvalidOperationException(
                    "Could not verify the integrity of the metadata file or accurately identify the metadata sub-version"
                );
            }

            // Load all the relevant metadata using offsets provided in the header
            Images = ReadMetadataArray<Il2CppImageDefinition>(Header.ImagesOffset, Header.ImagesSize, Header.Images);

            // As an additional sanity check, all images in the metadata should have Mono.Cecil.MetadataToken == 1
            // In metadata v24.1, two extra fields were added which will cause the below test to fail.
            // In that case, we can then adjust the version number and reload
            // Tokens were introduced in v19 - we don't bother testing earlier versions
            if (Version >= MetadataVersions.V190 && Images.Any(x => x.Token != 1))
                if (Version == MetadataVersions.V240)
                {
                    Version = MetadataVersions.V241;

                    // No need to re-read the header, it's the same for both sub-versions
                    Images = ReadMetadataArray<Il2CppImageDefinition>(Header.ImagesOffset, Header.ImagesSize, Header.Images);

                    if (Images.Any(x => x.Token != 1))
                        throw new InvalidOperationException("Could not verify the integrity of the metadata file image list");
                }

            if (Version.Major == 20 && Header.TypeDefinitionsSize % Sizeof<Il2CppTypeDefinition>() != 0)
                Version = MetadataVersions.V201;
            Types = ReadMetadataArray<Il2CppTypeDefinition>(Header.TypeDefinitionsOffset, Header.TypeDefinitionsSize, Header.TypeDefinitions);
            // Unity 5.3.4p1 / 5.4.0b12 inserts ccwFunctionIndex into Il2CppTypeDefinition while still reporting version 20.
            if (Version.Major == 20 && Version.Minor == 0 && Types.Any(static t => (t.Token & 0xFF000000) != 0x02000000))
            {
                Version = MetadataVersions.V201;
                Types = ReadMetadataArray<Il2CppTypeDefinition>(Header.TypeDefinitionsOffset, Header.TypeDefinitionsSize, Header.TypeDefinitions);
            }
            if (
                Version == MetadataVersions.V201
                && (Header.TypeDefinitionsSize % Sizeof<Il2CppTypeDefinition>() != 0 || Types.Any(static t => (t.Token & 0xFF000000) != 0x02000000))
            )
                throw new InvalidDataException("Metadata version 20 type definitions have an invalid record size or TypeDef token.");
            Methods = ReadMetadataArray<Il2CppMethodDefinition>(Header.MethodsOffset, Header.MethodsSize, Header.Methods);
            Params = ReadMetadataArray<Il2CppParameterDefinition>(Header.ParametersOffset, Header.ParametersSize, Header.Parameters);
            Fields = ReadMetadataArray<Il2CppFieldDefinition>(Header.FieldsOffset, Header.FieldsSize, Header.Fields);
            FieldDefaultValues = ReadMetadataArray<Il2CppFieldDefaultValue>(
                Header.FieldDefaultValuesOffset,
                Header.FieldDefaultValuesSize,
                Header.FieldDefaultValues
            );
            Properties = ReadMetadataArray<Il2CppPropertyDefinition>(Header.PropertiesOffset, Header.PropertiesSize, Header.Properties);
            Events = ReadMetadataArray<Il2CppEventDefinition>(Header.EventsOffset, Header.EventsSize, Header.Events);
            InterfaceUsageIndices = ReadMetadataArray<TypeIndex>(Header.InterfacesOffset, Header.InterfacesSize, Header.Interfaces);
            NestedTypeIndices = ReadMetadataPrimitiveArray<int>(Header.NestedTypesOffset, Header.NestedTypesSize, Header.NestedTypes);
            GenericContainers = ReadMetadataArray<Il2CppGenericContainer>(
                Header.GenericContainersOffset,
                Header.GenericContainersSize,
                Header.GenericContainers
            );
            GenericParameters = ReadMetadataArray<Il2CppGenericParameter>(
                Header.GenericParametersOffset,
                Header.GenericParametersSize,
                Header.GenericParameters
            );
            GenericConstraintIndices = ReadMetadataArray<TypeIndex>(
                Header.GenericParameterConstraintsOffset,
                Header.GenericParameterConstraintsSize,
                Header.GenericParameterConstraints
            );
            InterfaceOffsets = ReadMetadataArray<Il2CppInterfaceOffsetPair>(
                Header.InterfaceOffsetsOffset,
                Header.InterfaceOffsetsSize,
                Header.InterfaceOffsets
            );
            VTableMethodIndices = ReadMetadataPrimitiveArray<uint>(Header.VTableMethodsOffset, Header.VTableMethodsSize, Header.VtableMethods);

            // The uint8_t public-key blob replaces escaped strings when hashValueIndex is removed in 24.4.
            var useCompressedAssemblyPublicKeys = Version >= MetadataVersions.V244;
            // Unity 2020.2.0a15-b6 has version 27 tables but still uses the older assembly names and escaped public keys.
            if (
                Version == MetadataVersions.V270
                && Images.Length > 0
                && Header.AssembliesSize == (long)Images.Length * (Sizeof<Il2CppAssemblyDefinition>() + sizeof(int))
            )
            {
                throw new NotSupportedException("Metadata 27 with legacy assembly names belongs to a Unity prerelease and is not supported.");
            }
            {
                // In v24.4 hashValueIndex was removed from Il2CppAssemblyNameDefinition, which is a field in Il2CppAssemblyDefinition
                // The number of images and assemblies should be the same. If they are not, we deduce that we are using v24.4
                // Note the version comparison matches both 24.2 and 24.3 here since 24.3 is tested for during binary loading
                var assemblyCount = Header.AssembliesSize / Sizeof<Il2CppAssemblyDefinition>();
                var changedAssemblyDefStruct = false;
                if (
                    (Version == MetadataVersions.V241 || Version == MetadataVersions.V242 || Version == MetadataVersions.V243)
                    && assemblyCount < Images.Length
                )
                {
                    if (Version == MetadataVersions.V241)
                        changedAssemblyDefStruct = true;

                    Version = MetadataVersions.V244;
                    useCompressedAssemblyPublicKeys = true;
                }

                Assemblies = ReadMetadataArray<Il2CppAssemblyDefinition>(Header.AssembliesOffset, Header.AssembliesSize, Header.Assemblies);

                if (changedAssemblyDefStruct)
                    Version = new(24, 1, MetadataVersions.Tag2018);

                ParameterDefaultValues = ReadMetadataArray<Il2CppParameterDefaultValue>(
                    Header.ParameterDefaultValuesOffset,
                    Header.ParameterDefaultValuesSize,
                    Header.ParameterDefaultValues
                );
            }

            if (Version >= MetadataVersions.V180 && Version < MetadataVersions.V270)
            {
                MetadataUsageLists = ReadMetadataArray<Il2CppMetadataUsageList>(
                    Header.MetadataUsageListsOffset,
                    Header.MetadataUsageListsCount,
                    default
                );
                MetadataUsagePairs = ReadMetadataArray<Il2CppMetadataUsagePair>(
                    Header.MetadataUsagePairsOffset,
                    Header.MetadataUsagePairsCount,
                    default
                );
            }

            if (Version >= MetadataVersions.V180)
            {
                FieldRefs = ReadMetadataArray<Il2CppFieldRef>(Header.FieldRefsOffset, Header.FieldRefsSize, Header.FieldRefs);
            }

            if (Version >= MetadataVersions.V210 && Version < MetadataVersions.V290)
            {
                AttributeTypeIndices = ReadMetadataPrimitiveArray<int>(Header.AttributesTypesOffset, Header.AttributesTypesCount, default);
                AttributeTypeRanges = ReadMetadataArray<Il2CppCustomAttributeTypeRange>(
                    Header.AttributesInfoOffset,
                    Header.AttributesInfoCount,
                    default
                );
            }

            if (Version >= MetadataVersions.V290)
            {
                AttributeDataRanges = ReadMetadataArray<Il2CppCustomAttributeDataRange>(
                    Header.AttributeDataRangeOffset,
                    Header.AttributeDataRangeSize,
                    Header.AttributeDataRanges
                );
            }

            if (Version >= MetadataVersions.V1060)
            {
                TypeInlineArrays = ReadMetadataArray<Il2CppInlineArrayLength>(0, 0, Header.TypeInlineArrays);
            }

            {
                var stringOffset = Version >= MetadataVersions.V390 ? Header.Strings.Offset : Header.StringOffset;
                var stringLength = Version >= MetadataVersions.V390 ? Header.Strings.SectionSize : Header.StringSize;

                Position = stringOffset;

                while (Position < stringOffset + stringLength)
                    Strings.Add((int)Position - stringOffset, ReadNullTerminatedString());
            }

            {
                var stringLiteralList = ReadMetadataArray<Il2CppStringLiteral>(
                    Header.StringLiteralOffset,
                    Header.StringLiteralSize,
                    Header.StringLiterals
                );

                var dataOffset = Version >= MetadataVersions.V390 ? Header.StringLiteralData.Offset : Header.StringLiteralDataOffset;

                if (Version >= MetadataVersions.V390)
                {
                    var literals = new string[stringLiteralList.Length - 1];
                    for (var i = 0; i < literals.Length; i++)
                    {
                        var currentStringDataIndex = stringLiteralList[i].DataIndex;
                        var nextStringDataIndex = stringLiteralList[i + 1].DataIndex;
                        var stringLength = nextStringDataIndex - currentStringDataIndex;

                        literals[i] = ReadFixedLengthString(dataOffset + currentStringDataIndex, stringLength);
                    }

                    StringLiterals = literals;
                }
                else
                {
                    StringLiterals = new string[stringLiteralList.Length];
                    for (var i = 0; i < stringLiteralList.Length; i++)
                        StringLiterals[i] = ReadFixedLengthString(dataOffset + stringLiteralList[i].DataIndex, (int)stringLiteralList[i].Length);
                }
            }

            // These are stored in a special way, so we read them in advance.
            if (useCompressedAssemblyPublicKeys)
            {
                var stringOffset = Version >= MetadataVersions.V390 ? Header.Strings.Offset : Header.StringOffset;

                foreach (var assembly in Assemblies.Where(x => x.Aname.Flags.HasFlag(AssemblyNameFlags.PublicKey)))
                {
                    Position = stringOffset + assembly.Aname.PublicKeyIndex;
                    var length = ReadCompressedUInt32();

                    if (length > Length - Position)
                        throw new InvalidDataException("Metadata assembly public-key length exceeds the metadata file.");

                    AssemblyPublicKeys[assembly.Aname.PublicKeyIndex] = ReadBytes((int)length);
                }
            }
            else
            {
                foreach (var assembly in Assemblies.Where(x => x.Aname.Flags.HasFlag(AssemblyNameFlags.PublicKey)))
                {
                    var escapedPublicKey = Strings[assembly.Aname.PublicKeyIndex];
                    Debug.Assert(escapedPublicKey[0] == '"' && escapedPublicKey[^1] == '"');

                    var publicKeyHex = escapedPublicKey.AsSpan(1, escapedPublicKey.Length - 2);

                    // Upper bound for the public key length, this could also be decreased
                    var publicKey = new byte[publicKeyHex.Length];
                    var publicKeyCurrentIndex = 0;
                    var publicKeyCurrentByte = byte.MinValue;
                    foreach (var val in publicKeyHex)
                    {
                        switch (val)
                        {
                            case '\\':
                                publicKey[publicKeyCurrentIndex++] = publicKeyCurrentByte;
                                publicKeyCurrentByte = 0;
                                continue;
                            case 'x':
                                continue;
                            default:
                                var currentCharValue = byte.Parse(new ReadOnlySpan<char>(in val), NumberStyles.HexNumber);
                                publicKeyCurrentByte = (byte)((publicKeyCurrentByte << 4) | currentCharValue);
                                break;
                        }
                    }

                    publicKey[publicKeyCurrentIndex++] = publicKeyCurrentByte;
                    // The first backslash flushed the initial zero accumulator; it is not part of the key.
                    AssemblyPublicKeys[assembly.Aname.PublicKeyIndex] = publicKey[1..publicKeyCurrentIndex];
                }
            }
        }

        public ImmutableArray<T> ReadMetadataPrimitiveArray<T>(int oldOffset, int oldSize, Il2CppSectionMetadata newMetadata)
            where T : unmanaged
        {
            if (Version >= MetadataVersions.V390)
            {
                ValidateSection(newMetadata, Unsafe.SizeOf<T>(), typeof(T).Name);
                return ReadPrimitiveArray<T>(newMetadata.Offset, newMetadata.Count);
            }

            return ReadPrimitiveArray<T>(oldOffset, oldSize / Unsafe.SizeOf<T>());
        }

        public ImmutableArray<T> ReadMetadataArray<T>(int oldOffset, int oldSize, Il2CppSectionMetadata newMetadata)
            where T : IReadable, new()
        {
            if (Version >= MetadataVersions.V390)
            {
                ValidateSection(newMetadata, Sizeof<T>(), typeof(T).Name);
                return ReadVersionedObjectArray<T>(newMetadata.Offset, newMetadata.Count);
            }

            return ReadVersionedObjectArray<T>(oldOffset, oldSize / Sizeof<T>());
        }

        // Save metadata to file, overwriting if necessary
        public void SaveToFile(string pathname)
        {
            Position = 0;
            using var outFile = new FileStream(pathname, FileMode.Create, FileAccess.Write);
            CopyTo(outFile);
        }

        public int Sizeof<T>()
            where T : IReadable => T.Size(Version, new ReaderConfig(Is32Bit));
    }
}
