using System.Buffers.Binary;
using System.Collections.Immutable;

namespace Il2CppInspector.Tests.Common.IL2CPP;

internal static class MetadataVersionDetectionTests
{
    internal static void Run()
    {
        foreach (var bits in new[] { 32, 64 })
        {
            foreach (var version in new[] { 29, 31, 106, 108 })
            {
                using var source = version < 106 ? CreateLegacyMetadata(version) : RuntimeMetadataTests.CreateMetadata(version: version);
                var bytes = source.ToArray();
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), 0);
                using var binary = RuntimeMetadataTests.CreateBinary(bits, version);
                if (version < 106)
                {
                    binary.Position = 0x800 + 4 * (bits / 8);
                    using var writer = new BinaryWriter(binary, Encoding.UTF8, true);
                    if (bits == 32)
                        writer.Write(0x12520u);
                    else
                        writer.Write(0x12520ul);
                }
                var originalBinary = binary.ToArray();
                binary.Position = 17;
                ImmutableArray<int> candidates = [];
                GamePlugin plugin = new DetectionPlugin(found => candidates = found);
                plugin.Load(binary, bytes, null, new LoadOptions(), null);
                Check(
                    candidates.SequenceEqual(version == 106 ? new[] { 106, 107 } : new[] { version }),
                    $"Plugin metadata detection, {bits}-bit: erased version {version} yields only the jointly valid candidates"
                );
                Check(
                    binary.Position == 17
                        && binary.ToArray().SequenceEqual(originalBinary)
                        && BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4)) == 0,
                    $"Plugin metadata detection, {bits}-bit version {version}: borrowed binary and metadata inputs remain unchanged"
                );
            }
        }

        using var metadata = RuntimeMetadataTests.CreateMetadata();
        var data = metadata.ToArray();
        using var image = RuntimeMetadataTests.CreateBinary(64);
        GamePlugin probe = new DetectionPlugin(static found =>
            Check(found.SequenceEqual(new[] { 108 }), "Detection ignores a plausible but incorrect version word")
        );
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), 29);
        probe.Load(image, data, null, null, null);

        // Module/ParamDef RIDs can be nil for generated modules and parameters without explicit Param rows.
        using (var legacy = CreateLegacyMetadata(31))
        using (var native = RuntimeMetadataTests.CreateBinary(64, 31))
        {
            var bytes = legacy.ToArray();
            var imageOffset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0xA8));
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(imageOffset + 28), 0);
            var parametersOffset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0x58));
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(parametersOffset + 4), 0x08000000);
            native.Position = 0x800 + 4 * 8;
            using var writer = new BinaryWriter(native, Encoding.UTF8, true);
            writer.Write(0x12520ul);
            probe = new DetectionPlugin(static found =>
                Check(found.SequenceEqual(new[] { 31 }), "Nil Module and ParamDef RIDs remain valid during detection")
            );
            probe.Load(native, bytes, null, null, null);
        }

        probe = new DetectionPlugin(static found =>
            Check(found.IsEmpty, "A damaged method token cannot pass merely because the header length matches")
        );
        var methodsOffset = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(8 + 5 * 12));
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(methodsOffset + 12), 0x04000001);
        probe.Load(image, data, null, null, null);

        data = metadata.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), 0);
        using var erased = new MemoryStream(data);
        Check(
            Throws<NotSupportedException>(() => Inspector.LoadFromStream(image, erased, new LoadOptions())),
            "Normal auto-detect continues to reject an erased metadata version without invoking plugin version detection"
        );

        probe = new DetectionPlugin(static found =>
            Check(found.IsEmpty, "A valid metadata file with a mismatched native type table has no candidates")
        );
        image.Position = 0xA00 + 4 * 8;
        using (var writer = new BinaryWriter(image, Encoding.UTF8, true))
            writer.Write(0ul);
        probe.Load(image, data, null, null, null);

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Check(
            Throws<OperationCanceledException>(() => probe.Load(image, data, null, new LoadOptions { CancellationToken = cancelled.Token }, null)),
            "Plugin metadata detection propagates cancellation instead of treating it as a rejected candidate"
        );
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8), int.MaxValue);
        probe = new DetectionPlugin(static found =>
            Check(found.IsEmpty, "Out-of-file metadata header lengths cannot trigger speculative table allocations")
        );
        probe.Load(image, data, null, null, null);

        data = metadata.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8 + 5 * 12 + 8), int.MaxValue);
        probe = new DetectionPlugin(static found => Check(found.IsEmpty, "An impossible section count is rejected before reading method records"));
        probe.Load(image, data, null, null, null);
        BinaryPrimitives.WriteInt32LittleEndian(data, 0);
        Check(
            Throws<InvalidDataException>(() => probe.Load(image, data, null, null, null)),
            "Plugin version detection requires the caller to normalize a missing metadata signature first"
        );
    }

    private sealed class DetectionPlugin(Action<ImmutableArray<int>> receive) : GamePlugin
    {
        public override string GameId => "Detection_Test_Windows_0.0.0";

        public override bool Matches(ReadOnlySpan<byte> metadata) => false;

        public override Inspector Load(Stream binary, byte[] metadata, byte[] startupMetadata, LoadOptions options, EventHandler<string> status)
        {
            receive(DetectMetadataVersions(binary, metadata, options));
            return null;
        }
    }

    // The 29/31 headers have the same length, but MethodDefinition gains a return-parameter token in 31.
    private static MemoryStream CreateLegacyMetadata(int version)
    {
        var types = CppTypeCollection.FromUnityVersion(version == 29 ? "2022.3.32f1" : "2022.3.33f1");
        var header = types.GetComplexType("Il2CppGlobalMetadataHeader");
        var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write(Il2CppGlobalMetadataHeader.ExpectedSanity);
        writer.Write(version);
        writer.Write(new byte[header.SizeBytes - 8]);
        void Section(string name, Action write)
        {
            var start = (int)stream.Position;
            write();
            var end = stream.Position;
            stream.Position = header[name + "Offset"].OffsetBytes;
            writer.Write(start);
            writer.Write((int)(end - start));
            stream.Position = end;
        }
        void Record(string name, params (string Field, int Value)[] values)
        {
            var type = types.GetComplexType(name);
            var bytes = new byte[type.SizeBytes];
            foreach (var (field, value) in values)
            {
                var destination = bytes.AsSpan((int)type[field].OffsetBytes);
                if (type[field].SizeBytes == 2)
                    BinaryPrimitives.WriteUInt16LittleEndian(destination, unchecked((ushort)value));
                else
                    BinaryPrimitives.WriteInt32LittleEndian(destination, value);
            }
            writer.Write(bytes);
        }
        Section("stringLiteral", static () => { });
        Section("string", () => writer.Write("\0mscorlib.dll\0"u8));
        Section(
            "methods",
            () =>
            {
                for (var i = 0; i < 3; i++)
                    Record(
                        "Il2CppMethodDefinition",
                        ("nameIndex", 1),
                        ("parameterStart", i == 0 ? 0 : -1),
                        ("parameterCount", i == 0 ? 1 : 0),
                        ("genericContainerIndex", -1),
                        ("token", 0x06000001 + i),
                        ("flags", 0x16),
                        ("slot", -1)
                    );
            }
        );
        Section("parameters", () => Record("Il2CppParameterDefinition", ("nameIndex", 1), ("token", 0x08000001)));
        Section(
            "typeDefinitions",
            () =>
                Record(
                    "Il2CppTypeDefinition",
                    ("nameIndex", 1),
                    ("declaringTypeIndex", -1),
                    ("parentIndex", -1),
                    ("elementTypeIndex", -1),
                    ("genericContainerIndex", -1),
                    ("flags", 1),
                    ("fieldStart", -1),
                    ("eventStart", -1),
                    ("propertyStart", -1),
                    ("nestedTypesStart", -1),
                    ("method_count", 3),
                    ("token", 0x02000001)
                )
        );
        Section(
            "images",
            () =>
                Record("Il2CppImageDefinition", ("nameIndex", 1), ("typeCount", 1), ("exportedTypeStart", -1), ("entryPointIndex", -1), ("token", 1))
        );
        Section(
            "assemblies",
            () =>
            {
                var size = types.GetComplexType("Il2CppAssemblyDefinition").SizeBytes;
                writer.Write(0);
                writer.Write(0x20000001u);
                writer.Write(0);
                writer.Write(0);
                writer.Write(1);
                writer.Write(new byte[size - 20]);
            }
        );
        stream.Position = 0;
        return stream;
    }
}
