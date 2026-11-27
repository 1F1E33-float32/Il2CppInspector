using Il2CppInspector.Next;
using VersionedSerialization;

namespace Il2CppInspector.Tests.Common.Cpp;

internal static class UnityHeadersTests
{
    internal static void Run(string clang = null)
    {
        IL2CPP.RuntimeMetadataTests.Run();
        var resources = UnityHeaders.GetAllTypeHeaders().OrderBy(static r => r.VersionRange).ToArray();
        var apis = UnityHeaders.GetAllAPIHeaders().OrderBy(static r => r.VersionRange).ToArray();
        foreach (var group in new[] { resources, apis })
        {
            Check(
                group.All(static r =>
                    r.VersionRange.Max != null
                    && r.VersionRange.Min.BuildType is UnityVersion.BuildTypeEnum.Final or UnityVersion.BuildTypeEnum.Patch
                    && r.VersionRange.Max.BuildType is UnityVersion.BuildTypeEnum.Final or UnityVersion.BuildTypeEnum.Patch
                ) && group.Zip(group.Skip(1)).All(static pair => pair.First.VersionRange.Intersect(pair.Second.VersionRange) == null),
                $"{group[0].Name}: released header ranges are bounded, exclude prerelease endpoints and do not overlap"
            );
        }

        foreach (var resource in resources)
        {
            var headers = UnityHeaders.GetHeadersForVersion(resource.VersionRange.Min);
            foreach (var bits in new[] { 32, 64 })
            {
                var types = new CppTypeCollection(bits);
                types.AddFromDeclarationText(headers.GetTypeHeaderText(bits));
                types.AddFromDeclarationText(headers.GetAPIHeaderTypedefText());
                var klass = types.GetComplexType("Il2CppClass");
                var merged = types.GetComplexType("Il2CppClass_Merged");
                var mismatches = klass
                    .Flattened.Where(static field => field.Name != "cctor_thread")
                    .Where(field =>
                        !merged.Flattened.Any(candidate =>
                            candidate.Name == field.Name && candidate.Offset == field.Offset && candidate.Size == field.Size
                        )
                    )
                    .Select(static field => $"{field.Name} at {field.OffsetBytes}")
                    .ToArray();
                Check(
                    mismatches.Length == 0,
                    $"Unity {resource.VersionRange}, {bits}-bit: split and complete Il2CppClass layouts agree {string.Join(", ", mismatches)}"
                );
                var version = headers.MetadataVersion;
                var config = new ReaderConfig(bits == 32);
                var recordSizes = new (string Name, int Size)[]
                {
                    ("Il2CppTypeDefinition", Il2CppTypeDefinition.StructSize(version, config)),
                    ("Il2CppMethodDefinition", Il2CppMethodDefinition.StructSize(version, config)),
                    ("Il2CppImageDefinition", Il2CppImageDefinition.StructSize(version, config)),
                    ("Il2CppFieldDefinition", Il2CppFieldDefinition.StructSize(version, config)),
                    ("Il2CppParameterDefinition", Il2CppParameterDefinition.StructSize(version, config)),
                    ("Il2CppCodeRegistration", Il2CppCodeRegistration.StructSize(version, config)),
                    ("Il2CppMetadataRegistration", Il2CppMetadataRegistration.StructSize(version, config)),
                    ("Il2CppGenericClass", Il2CppGenericClass.StructSize(version, config)),
                    ("Il2CppRGCTXDefinition", Il2CppRgctxDefinition.StructSize(version, config)),
                };
                Check(
                    recordSizes.All(record => types.GetComplexType(record.Name).SizeBytes == record.Size),
                    $"Unity {resource.VersionRange}, {bits}-bit: serialized records match the native definitions"
                );
                if (version >= MetadataVersions.V270)
                    Check(
                        types.GetComplexType("Il2CppCodeGenModule").SizeBytes == Il2CppCodeGenModule.StructSize(version, config),
                        $"Unity {resource.VersionRange}, {bits}-bit: the module initializer occupies one pointer"
                    );
            }
            Check(
                UnityHeaders.GetHeadersForVersion(resource.VersionRange.Max).TypeHeaderResource.Name == resource.Name,
                $"Unity {resource.VersionRange.Max}: upper boundary selects the same type layout"
            );
            if (clang != null)
            {
                var directory = TestEnvironment.RepositoryPath("artifacts", "unity-headers");
                Directory.CreateDirectory(directory);
                var fixture = Path.Combine(directory, "layout.cpp");
                File.WriteAllText(
                    fixture,
                    "#include <stdint.h>\n#include <stddef.h>\n#if __SIZEOF_POINTER__ == 4\n#define IS_32BIT\n#endif\n"
                        + resource.GetText()
                        + "\nstatic_assert(offsetof(Il2CppClass, instance_size) == offsetof(Il2CppClass_Merged, _1.instance_size));\n"
                        + "static_assert(offsetof(Il2CppClass, vtable_count) == offsetof(Il2CppClass_Merged, _1.vtable_count));\n"
                        + "static_assert(offsetof(Il2CppClass, vtable) == offsetof(Il2CppClass_Merged, vtable));\n"
                );
                foreach (var target in new[] { "i686-linux-gnu", "armv7-linux-gnueabihf", "aarch64-linux-gnu", "x86_64-pc-windows-msvc" })
                {
                    var (exitCode, _, errors) = TestEnvironment.RunProcess(
                        clang,
                        ["--target=" + target, "-std=c++17", "-ffreestanding", "-fms-extensions", "-fsyntax-only", fixture]
                    );
                    Check(exitCode == 0, $"Unity {resource.VersionRange}: clang verifies {target}: {errors}");
                }
            }
        }

        foreach (
            var (unity, major, tag) in new (string, int, string)[]
            {
                ("5.3.1f1", 16, null),
                ("5.3.1p1", 18, null),
                ("5.3.1p2", 18, null),
                ("5.3.1p3", 19, null),
                ("5.3.2p1", 20, null),
                ("5.3.4p2", 21, null),
                ("2018.4.18f1", 24, MetadataVersions.Tag2018),
                ("2020.2.0f1", 27, null),
                ("2021.1.0f1", 27, null),
                ("2022.1.0f1", 29, MetadataVersions.Tag2022),
                ("6000.3.0f1", 39, null),
                ("6000.5.0f1", 106, null),
                ("2021.3.39f1", 29, null),
                ("2021.3.40f1", 31, null),
                ("2022.3.32f1", 29, MetadataVersions.Tag2022),
                ("2022.3.33f1", 31, MetadataVersions.Tag2022),
                ("6000.0.9f1", 29, MetadataVersions.Tag2022),
                ("6000.0.10f1", 31, MetadataVersions.Tag2022),
                ("6000.5.1f1", 107, null),
                ("6000.5.11f1", 107, null),
                ("6000.6.0f1", 108, null),
                ("6000.6.11f1", 108, null),
            }
        )
        {
            var version = UnityHeaders.GetHeadersForVersion(unity).MetadataVersion;
            Check(version.Major == major && version.Tag == tag, $"Unity {unity}: metadata version and registration layout match the branch");
        }

        Check(UnityHeaders.GetHeadersForVersion("5.3.1").MetadataVersion.Major == 16, "A suffix-less Unity version selects the first final release");
        Check(
            UnityHeaders.GetHeadersForVersion("5.3.4p1").MetadataVersion == MetadataVersions.V201,
            "Unity 5.3.4p1 selects the CCW variant of metadata 20"
        );
        // A prerelease can fall numerically inside a released patch range; the suffix must still be rejected.
        foreach (var unity in new[] { "5.4.0b1", "2020.2.0a15", "2021.1.0b5", "2022.3.5b1", "2022.3.5rc1", "6000.3.0b1", "6000.6.0a2" })
        {
            Check(
                Throws<NotSupportedException>(() => UnityHeaders.GetTypeHeaderForVersion(unity))
                    && Throws<NotSupportedException>(() => UnityHeaders.GetAPIHeaderForVersion(unity)),
                $"Unity {unity}: prerelease suffixes cannot select released type or API headers"
            );
        }

        foreach (var version in new[] { 17, 25, 26, 28, 30, 32, 35, 36, 37, 38, 40, 103, 104, 105, 109, 110, 111, 112 })
        {
            using var data = new MemoryStream(new byte[512]);
            using var writer = new BinaryWriter(data, Encoding.UTF8, true);
            writer.Write(Il2CppGlobalMetadataHeader.ExpectedSanity);
            writer.Write(version);
            Check(
                Throws<NotSupportedException>(() => Metadata.FromStream(data)),
                $"Metadata {version}: unsupported formats fail before table decoding"
            );
        }

        var ccwTypes = CppTypeCollection.FromUnityVersion("5.3.4p1");
        var metadataHeader = ccwTypes.GetComplexType("Il2CppGlobalMetadataHeader");
        var definition = ccwTypes.GetComplexType("Il2CppTypeDefinition");
        // 28 new records occupy exactly as many bytes as 29 old records, so size alone cannot identify this variant.
        foreach (var count in new[] { 1, 28 })
        {
            var tableSize = definition.SizeBytes * count;
            using var data = new MemoryStream(new byte[metadataHeader.SizeBytes + tableSize + 3]);
            using var writer = new BinaryWriter(data, Encoding.UTF8, true);
            writer.Write(Il2CppGlobalMetadataHeader.ExpectedSanity);
            writer.Write(20);
            foreach (
                var (field, value) in new[]
                {
                    ("stringLiteralOffset", metadataHeader.SizeBytes),
                    ("stringOffset", metadataHeader.SizeBytes + tableSize),
                    ("stringCount", 3),
                    ("typeDefinitionsOffset", metadataHeader.SizeBytes),
                    ("typeDefinitionsCount", tableSize),
                }
            )
            {
                data.Position = metadataHeader[field].OffsetBytes;
                writer.Write(value);
            }
            for (var i = 0; i < count; i++)
            {
                data.Position = metadataHeader.SizeBytes + i * definition.SizeBytes + definition["nameIndex"].OffsetBytes;
                writer.Write(1);
                data.Position = metadataHeader.SizeBytes + i * definition.SizeBytes + definition["token"].OffsetBytes;
                writer.Write(0x02000001 + i);
            }
            data.Position = metadataHeader.SizeBytes + tableSize;
            writer.Write(new byte[] { 0, (byte)'T', 0 });
            var metadata = Metadata.FromStream(data);
            Check(
                metadata.Version == MetadataVersions.V201 && metadata.Types.Length == count && metadata.Types[^1].Token == 0x02000000 + count,
                $"Metadata 20 identifies the CCW variant with {count} type definitions"
            );
        }

        // Metadata 39 can contain fields but no implemented interfaces. The field section still determines TypeIndex width.
        using (var data = new MemoryStream(new byte[394]))
        {
            using var writer = new BinaryWriter(data, Encoding.UTF8, true);
            writer.Write(Il2CppGlobalMetadataHeader.ExpectedSanity);
            writer.Write(39);
            foreach (var (section, offset, size, count) in new[] { (0, 380, 4, 1), (1, 384, 0, 0), (2, 393, 1, 1), (11, 384, 9, 1) })
            {
                data.Position = 8 + section * 12;
                writer.Write(offset);
                writer.Write(size);
                writer.Write(count);
            }
            data.Position = 389;
            writer.Write(0x04000001);
            var metadata = Metadata.FromStream(data);
            Check(
                metadata.Fields.Single().Token == 0x04000001 && TypeIndex.Size(metadata.Version) == 1,
                "Metadata 39 infers a compact TypeIndex without an interface-offset table"
            );
        }

        foreach (var (unity, version, compressed) in new[] { ("2019.1.0f2", 24, false), ("2019.4.14f1", 24, false), ("2019.4.15f1", 24, true) })
        {
            var types = CppTypeCollection.FromUnityVersion(unity);
            var header = types.GetComplexType("Il2CppGlobalMetadataHeader");
            var image = types.GetComplexType("Il2CppImageDefinition");
            var assembly = types.GetComplexType("Il2CppAssemblyDefinition");
            var assemblyName = types.GetComplexType("Il2CppAssemblyNameDefinition");
            var stringOffset = header.SizeBytes + image.SizeBytes + assembly.SizeBytes;
            byte[] keyData = compressed ? [2, 1, 2, 0] : Encoding.UTF8.GetBytes("\"\\x01\\x02\"\0");
            using var data = new MemoryStream(new byte[stringOffset + keyData.Length]);
            using var writer = new BinaryWriter(data, Encoding.UTF8, true);
            writer.Write(Il2CppGlobalMetadataHeader.ExpectedSanity);
            writer.Write(version);
            foreach (
                var (field, value) in new[]
                {
                    ("stringLiteralOffset", header.SizeBytes),
                    ("stringOffset", stringOffset),
                    ("stringCount", keyData.Length),
                    ("imagesOffset", header.SizeBytes),
                    ("imagesCount", image.SizeBytes),
                    ("assembliesOffset", header.SizeBytes + image.SizeBytes),
                    ("assembliesCount", assembly.SizeBytes),
                }
            )
            {
                data.Position = header[field].OffsetBytes;
                writer.Write(value);
            }
            data.Position = header.SizeBytes + image["token"].OffsetBytes;
            writer.Write(1);
            data.Position = header.SizeBytes + image.SizeBytes + assembly["aname"].OffsetBytes + assemblyName["flags"].OffsetBytes;
            writer.Write(1);
            data.Position = stringOffset;
            writer.Write(keyData);
            var metadata = Metadata.FromStream(data);
            Check(
                metadata.AssemblyPublicKeys[0].SequenceEqual(new byte[] { 1, 2 }),
                $"Unity {unity}: assembly public-key encoding matches its metadata layout"
            );
        }

        // MetadataDeserialization::DeserializeGenericContainer in Unity 6000.5.0f1 serializes 4 + 2 + 1 + index bytes.
        byte[] containerData = [0x78, 0x56, 0x34, 0x12, 0x2C, 0x01, 0x01, 0xF4, 0x01];
        var reader = Reader.LittleEndian(containerData);
        StructVersion compactVersion = new(106, 0, "GenericParameterIndex2");
        var container = reader.ReadVersionedObject<Il2CppGenericContainer>(compactVersion);
        Check(
            container.OwnerIndex == 0x12345678 && container.TypeArgc == 300 && container.IsMethod == 1 && container.GenericParameterStart == 500,
            "Metadata 106 decodes compact generic-container fields without losing their normalized values"
        );
        Check(
            Il2CppGlobalMetadataHeader.StructSize(new StructVersion(39)) == 380
                && Il2CppGlobalMetadataHeader.StructSize(new StructVersion(106)) == 392,
            "Metadata 39 and 106 use section descriptors and include the inline-array section at its correct boundary"
        );
        foreach (var is32Bit in new[] { true, false })
        {
            var pointerSize = is32Bit ? 4 : 8;
            var config = new ReaderConfig(is32Bit);
            var pointerReader = Reader.LittleEndian(new byte[] { 0x78, 0x56, 0x34, 0x12, 0, 0, 0, 0 }.AsSpan(0, pointerSize), config: config);
            var pointer = pointerReader.ReadVersionedObject<Il2CppMethodPointer>();
            Check(
                pointer.Value == 0x12345678 && Il2CppMethodPointer.StructSize(default, config) == pointerSize,
                $"{pointerSize * 8}-bit native method pointers consume exactly one address"
            );
        }
        var rgctxReader = Reader.LittleEndian(new byte[] { 1, 0, 0, 0, 42, 0, 0, 0, 2, 0, 0, 0, 84, 0, 0, 0 });
        var rgctx = rgctxReader.ReadVersionedObjectArray<Il2CppRgctxDefinition>(2, MetadataVersions.V271);
        Check(
            (int)rgctx[0].Type == 1 && rgctx[0].Data.PointerValue == 42 && (int)rgctx[1].Type == 2 && rgctx[1].Data.PointerValue == 84,
            "Pre-27.2 RGCTX arrays use inline 32-bit indices even in a 64-bit binary"
        );
    }
}
