using System.Buffers.Binary;
using Il2CppInspector.Next;
using VersionedSerialization;

namespace Il2CppInspector.Tests.Common.IL2CPP;

internal static class RuntimeMetadataTests
{
    internal static void Run()
    {
        VerifyVersion18();
        using (var bytes = CreateMetadata(version: 107))
        {
            var metadata = Metadata.FromStream(bytes);
            Check(
                metadata.Version.Major == 107
                    && metadata.Images.Single().TypeCount == 1
                    && metadata.Methods.Length == 3
                    && metadata.Types.Single().MethodCount == 3,
                "Metadata 107 decodes the compact image, type and method records retained from 106"
            );
        }
        foreach (var bits in new[] { 32, 64 })
        {
            using var metadataBytes = CreateMetadata();
            var metadata = Metadata.FromStream(metadataBytes);
            using var binaryBytes = CreateBinary(bits);
            var image = PEReader.Load(binaryBytes);
            var binary = Il2CppBinary.Load(image, metadata);
            var package = new Inspector(binary, metadata);
            Check(
                binary.CodeRegistrationPointer == Address(0x800) && binary.MetadataRegistrationPointer == Address(0xA00),
                $"Metadata 108, {bits}-bit: scanning locates the shortened registration with both usage tables"
            );
            Check(
                binary
                    .MethodSpecs.Select(static s => (s.MethodDefinitionIndex, s.ClassIndexIndex, s.MethodIndexIndex))
                    .SequenceEqual(new[] { (0, 0, -1), (1, -1, 0), (2, 0, 0) }),
                $"Metadata 108, {bits}-bit: all three generic method categories preserve their global indices"
            );
            var module = binary.Modules["mscorlib.dll"];
            Check(
                binary.MethodInvokerIndices[module].SequenceEqual(new[] { 1, -1, 0 })
                    && binary.GenericMethodPointers[binary.MethodSpecs[0]] == Address(0x1600)
                    && binary.GenericMethodPointers[binary.MethodSpecs[2]] == Address(0x1640)
                    && binary.GenericMethodInvokerIndices[binary.MethodSpecs[1]] == -1,
                $"Metadata 108, {bits}-bit: compact invokers and both generic function tables resolve native addresses"
            );
            var generic = image.ReadMappedVersionedObject<Il2CppGenericClass>(Address(0x1300));
            Check(
                generic.Context.ClassInst.PointerValue == Address(0x1340) && generic.Context.MethodInst.Null && generic.CachedClass == 0,
                $"Metadata 108, {bits}-bit: generic class normalization excludes the removed method context pointer"
            );
            Check(
                package.MetadataUsages.Any(static u =>
                    u.VirtualAddress == Address(0x1500) && u.Type == MetadataUsageType.MethodDef && u.SourceIndex == 2
                )
                    && package.MetadataUsages.Any(u =>
                        u.VirtualAddress == Address(0x1500 + bits / 8) && u.Type == MetadataUsageType.MethodRef && u.SourceIndex == 2
                    ),
                $"Metadata 108, {bits}-bit: shifted metadata usage kinds retain method and generic-method references"
            );
            Check(
                UnityHeaders.GuessHeadersForBinary(binary).All(static h => h.MetadataVersion == MetadataVersions.V1080),
                $"Metadata 108, {bits}-bit: automatic header selection stays on the matching released layout"
            );
        }

        // The invalid index occupies the maximum unsigned value; count 255 fits one byte and count 256 needs two.
        foreach (var (count, expectedWidth) in new[] { (255, 1), (256, 2), (65535, 2), (65536, 4) })
        {
            using var bytes = CreateMetadata(count);
            var metadata = Metadata.FromStream(bytes);
            var specs = metadata.ReadMethodSpecs(count);
            Check(
                specs[2].ClassIndexIndex == count - 1
                    && specs[2].MethodIndexIndex == count - 1
                    && metadata.Header.MethodSpecs.SectionSize == 1 + expectedWidth * 2,
                $"Metadata 108: generic instance indices use {expectedWidth} bytes for {count} entries"
            );
        }
        using (var bytes = CreateMetadata())
        {
            var metadata = Metadata.FromStream(bytes);
            bytes.Position = metadata.Header.MethodSpecs.Offset;
            bytes.WriteByte(99);
            var malformed = Metadata.FromStream(bytes);
            Check(
                Throws<InvalidDataException>(() => malformed.ReadMethodSpecs(1)),
                "Metadata 108 rejects method indices outside the definition table"
            );
            bytes.SetLength(metadata.Header.InvokerIndices.Offset + 1);
            var truncated = Metadata.FromStream(bytes);
            Check(
                Throws<InvalidDataException>(() => truncated.ReadInvokerIndices(truncated.Images[0], 3, 2)),
                "Metadata 108 rejects truncated invoker sections"
            );
        }

        var reader = Reader.LittleEndian(new byte[] { 6, 0, 0, 0, 17, 0, 0, 0, 7, 0, 0, 0, 23, 0, 0, 0 });
        var rgctx = reader.ReadVersionedObjectArray<Il2CppRgctxDefinition>(2, MetadataVersions.V1080);
        Check(
            (int)rgctx[1].Type == 7 && rgctx[0].Data.PointerValue == 17 && rgctx[1].Data.PointerValue == 23,
            "Metadata 108 RGCTX records retain inline constrained-call and field-offset indices"
        );
        var oldUsage = Il2CppMetadataUsage.FromValue(MetadataVersions.V180, (6u << 29) | 37);
        var newUsage = Il2CppMetadataUsage.FromValue(MetadataVersions.V1080, (5u << 29) | (37u << 1) | 1);
        Check(
            oldUsage.Type == Il2CppMetadataUsageType.MethodRef
                && oldUsage.Index == 37
                && newUsage.Type == oldUsage.Type
                && newUsage.Index == oldUsage.Index,
            "Metadata 18 and 108 normalize different usage encodings to the same generic method reference"
        );
    }

    // Unity 6000.6.0f1: 41 section descriptors and packed records from MetadataDeserialization.
    internal static MemoryStream CreateMetadata(int genericInstanceCount = 1, int version = 108)
    {
        var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write(Il2CppGlobalMetadataHeader.ExpectedSanity);
        writer.Write(version);
        writer.Write(new byte[(version == 108 ? 41 : 32) * 12]);
        void Section(int index, int count, Action<BinaryWriter> write)
        {
            var start = checked((int)stream.Position);
            write(writer);
            var end = stream.Position;
            stream.Position = 8 + index * 12;
            writer.Write(start);
            writer.Write(checked((int)(end - start)));
            writer.Write(count);
            stream.Position = end;
        }
        Section(0, 1, static w => w.Write(0));
        Section(2, 14, static w => w.Write(Encoding.UTF8.GetBytes("\0mscorlib.dll\0")));
        Section(
            5,
            3,
            static w =>
            {
                for (uint i = 0; i < 3; i++)
                {
                    w.Write(1); // nameIndex
                    w.Write((byte)0); // declaringType
                    w.Write((byte)0); // returnType
                    w.Write(0u); // returnParameterToken
                    w.Write(byte.MaxValue); // parameterStart
                    w.Write(byte.MaxValue); // genericContainerIndex
                    w.Write(0x06000001u + i);
                    w.Write((ushort)0x16); // public static
                    w.Write((ushort)0);
                    w.Write(ushort.MaxValue);
                    w.Write((ushort)0);
                }
            }
        );
        Section(16, 1, static w => w.Write((byte)0));
        Section(
            19,
            1,
            static w =>
            {
                w.Write(1); // nameIndex
                w.Write(0); // namespaceIndex
                w.Write((byte)0); // byvalTypeIndex
                w.Write(byte.MaxValue); // declaringType
                w.Write(byte.MaxValue); // parent
                w.Write(byte.MaxValue); // genericContainer
                w.Write(1u); // flags
                w.Write(byte.MaxValue); // fields
                w.Write((byte)0); // methods
                w.Write(byte.MaxValue); // events
                w.Write(byte.MaxValue); // properties
                w.Write(byte.MaxValue); // nestedTypes
                w.Write((byte)0); // interfaces
                w.Write(0); // vtable
                w.Write(byte.MaxValue); // interfaceOffsets
                w.Write((ushort)3); // methodCount
                for (var i = 0; i < 7; i++)
                    w.Write((ushort)0);
                w.Write(0u); // bitfield
                w.Write(0x02000001u);
            }
        );
        Section(
            21,
            1,
            w =>
            {
                w.Write(1); // nameIndex
                w.Write(0); // assemblyIndex
                w.Write((byte)0); // typeStart
                w.Write(1u);
                w.Write(byte.MaxValue); // exportedTypeStart
                w.Write(0u);
                w.Write(byte.MaxValue); // entryPoint
                w.Write(1u); // token
                w.Write(0); // customAttributeStart
                w.Write(0u);
                if (version < 108)
                    return;
                w.Write(0u); // invokerIndicesStart
                w.Write(0u); // rgctxRangesStart
                w.Write(0u);
                w.Write((byte)0); // staticConstructorStart
                w.Write(0u);
            }
        );
        Section(
            22,
            1,
            static w =>
            {
                w.Write(0); // imageIndex
                w.Write(0x20000001u); // assembly token
                w.Write(1u); // module token
                w.Write(0); // referencedAssemblyStart
                w.Write(0); // referencedAssemblyCount
                w.Write(1); // assembly name
                w.Write(new byte[44]); // remaining assembly-name fields
            }
        );
        var instanceWidth =
            genericInstanceCount <= byte.MaxValue ? 1
            : genericInstanceCount <= ushort.MaxValue ? 2
            : 4;
        void Instance(BinaryWriter w)
        {
            var index = genericInstanceCount - 1;
            if (instanceWidth == 1)
                w.Write((byte)index);
            else if (instanceWidth == 2)
                w.Write((ushort)index);
            else
                w.Write(index);
        }
        if (version >= 108)
        {
            Section(
                32,
                1,
                w =>
                {
                    w.Write((byte)0);
                    Instance(w);
                }
            );
            Section(
                33,
                1,
                w =>
                {
                    w.Write((byte)1);
                    Instance(w);
                }
            );
            Section(
                34,
                1,
                w =>
                {
                    w.Write((byte)2);
                    Instance(w);
                    Instance(w);
                }
            );
            Section(35, 2, static w => w.Write(new byte[] { 0, 0, 1, 1, 1, 255 }));
            Section(36, 1, static w => w.Write(new byte[] { 2, 2, 0, 0 }));
            Section(37, 3, static w => w.Write(new byte[] { 1, 255, 0 }));
        }
        stream.Position = 0;
        return stream;
    }

    private static ulong Address(int fileOffset) => 0x10000ul + 0x1000 - 0x200 + (ulong)fileOffset;

    private static void VerifyVersion18()
    {
        var types = CppTypeCollection.FromUnityVersion("5.3.1p1");
        var header = types.GetComplexType("Il2CppGlobalMetadataHeader");
        var definition = types.GetComplexType("Il2CppTypeDefinition");
        var image = types.GetComplexType("Il2CppImageDefinition");
        var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload, Encoding.UTF8, true);
        writer.Write(Il2CppGlobalMetadataHeader.ExpectedSanity);
        writer.Write(18);
        writer.Write(new byte[header.SizeBytes - 8]);
        void Section(string name, Action<BinaryWriter> write)
        {
            var start = checked((int)payload.Position);
            write(writer);
            var end = payload.Position;
            payload.Position = header[name + "Offset"].OffsetBytes;
            writer.Write(start);
            writer.Write(checked((int)(end - start)));
            payload.Position = end;
        }
        Section("stringLiteral", static _ => { });
        Section("string", static w => w.Write(Encoding.UTF8.GetBytes("\0T\0")));
        Section(
            "typeDefinitions",
            w =>
            {
                w.Write(1);
                w.Write(new byte[definition.SizeBytes - 4]);
            }
        );
        Section(
            "images",
            w =>
            {
                w.Write(1);
                w.Write(new byte[image.SizeBytes - 4]);
            }
        );
        Section(
            "metadataUsageLists",
            static w =>
            {
                w.Write(0u);
                w.Write(1u);
            }
        );
        Section(
            "metadataUsagePairs",
            static w =>
            {
                w.Write(0u);
                w.Write((6u << 29) | 37u);
            }
        );
        Section(
            "fieldRefs",
            static w =>
            {
                w.Write(0);
                w.Write(0);
            }
        );
        using (payload)
        {
            var metadata = Metadata.FromStream(payload);
            Check(
                metadata.Types.Single().Token == 0
                    && metadata.Images.Single().Token == 0
                    && metadata.MetadataUsageLists.Single().Count == 1
                    && metadata.MetadataUsagePairs.Single().EncodedSourceIndex == ((6u << 29) | 37u)
                    && metadata.FieldRefs.Length == 1,
                "Metadata 18 loads usage and field-reference sections while retaining token-free type and image records"
            );
        }
    }

    // A single mapped PE section is enough to exercise the normal scanner in both pointer widths.
    internal static MemoryStream CreateBinary(int bits, int version = 108)
    {
        var data = new byte[0x2000];
        void U16(int offset, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset), value);
        void U32(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset), value);
        void Words(int offset, params ulong[] values)
        {
            foreach (var value in values)
            {
                if (bits == 32)
                    U32(offset, checked((uint)value));
                else
                    BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(offset), value);
                offset += bits / 8;
            }
        }
        U16(0, 0x5A4D);
        U32(0x3C, 0x80);
        U32(0x80, 0x4550);
        U16(0x84, bits == 32 ? (ushort)0x14C : (ushort)0x8664);
        U16(0x86, 1);
        U16(0x94, bits == 32 ? (ushort)0xE0 : (ushort)0xF0);
        U16(0x96, 0x2022);
        U16(0x98, bits == 32 ? (ushort)0x10B : (ushort)0x20B);
        U32(0x98 + 20, 0x1000);
        if (bits == 32)
            U32(0x98 + 28, 0x10000);
        else
            Words(0x98 + 24, 0x10000);
        U32(0x98 + 32, 0x1000);
        U32(0x98 + 36, 0x200);
        U32(0x98 + 56, 0x3000);
        U32(0x98 + 60, 0x200);
        U32(0x98 + (bits == 32 ? 92 : 108), 16);
        var section = 0x98 + (bits == 32 ? 0xE0 : 0xF0);
        Encoding.ASCII.GetBytes(".text").CopyTo(data, section);
        U32(section + 8, 0x1E00);
        U32(section + 12, 0x1000);
        U32(section + 16, 0x1E00);
        U32(section + 20, 0x200);
        U32(section + 36, 0x60000020);
        Words(0x800, 0, 0, 3, Address(0x1400), 0, 2, Address(0x1440), 0, 0, 0, 0, 0, 0, 0, 0, 1, Address(0xE00));
        Words(
            0xA00,
            1,
            Address(0x1200),
            1,
            Address(0x1220),
            1,
            Address(0x1240),
            1,
            Address(0x1260),
            1,
            Address(0x1280),
            1,
            Address(0x12A0),
            1,
            Address(0x12C0)
        );
        Words(0xC00, Address(0xF00), 3, Address(0x1400), 0, 0, 0, 0, 0, 0, 0, 0);
        Words(0xE00, Address(0xC00));
        Encoding.ASCII.GetBytes("mscorlib.dll\0").CopyTo(data, 0xF00);
        Words(0x1200, Address(0x1300));
        Words(0x1220, Address(0x1340));
        Words(0x1240, Address(0x1380));
        Words(0x1260, 0);
        Words(0x1280, Address(0x13C0));
        Words(0x12A0, Address(0x1500));
        Words(0x12C0, Address(0x1500));
        Words(0x1300, Address(0x1380), Address(0x1340), 0);
        Words(0x1340, 0, 0);
        Words(0x1380, 0);
        U32(0x1380 + bits / 8, 0x00120000); // IL2CPP_TYPE_CLASS
        U32(0x13C0, 16);
        Words(0x1400, Address(0x1600), Address(0x1620), Address(0x1640));
        Words(0x1440, Address(0x1660), Address(0x1680));
        Words(0x1500, (2u << 29) | 5u, (5u << 29) | 5u);
        if (version < 108)
        {
            // Unity 6000.5 retains method specs and generic functions in native registration tables.
            Words(
                0xA00,
                1,
                Address(0x1200),
                1,
                Address(0x1220),
                1,
                Address(0x1700),
                1,
                Address(0x1240),
                1,
                Address(0x1740),
                1,
                Address(0x1260),
                1,
                Address(0x1280),
                0,
                0
            );
            Words(0xC00, Address(0xF00), 3, Address(0x1400), 0, 0, Address(0x1780), 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
            Words(0x1300, Address(0x1380), Address(0x1340), 0, 0);
            U32(0x1740, 0); // method definition
            U32(0x1744, 0); // class instance
            U32(0x1748, uint.MaxValue); // absent method instance
            U32(0x1780, 1);
            U32(0x1784, uint.MaxValue);
            U32(0x1788, 0);
        }
        return new MemoryStream(data);
    }
}
