using System.Buffers.Binary;
using Iced.Intel;
using UnicornEngine;
using UnicornEngine.Const;

namespace Il2CppInspector.Plugin.AzurPromilia;

internal static class AzurPromiliaMetadata
{
    private const long StackAddress = 0x700000000000;
    private const long StackSize = 0x100000;
    private const long MetadataAddress = 0x710000000000;
    private const int MaximumMetadataSize = 256 * 1024 * 1024;
    private const int PayloadOffset = 0x148;
    private const int BlockSize = 1024 * 1024;

    // GlobalMetadata::Initialize at RVA 0x14EBDD0 stores the mapped file, then calls the
    // Code Philosophy decoder at RVA 0x146D1F0. Locate both calls through the filename xref.
    internal static List<(ulong Start, ulong End, ulong Global)> FindInitializer(
        IFileFormatStream image,
        CancellationToken cancellationToken = default
    )
    {
        HashSet<ulong> names = [];
        foreach (var section in image.GetSections().Where(static section => !section.IsExec && !section.IsBSS))
        {
            for (var offset = 0; offset < section.ImageLength; offset += BlockSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var bytes = image.ReadBytes(section.ImageStart + offset, Math.Min(BlockSize + 19, section.ImageLength - offset));
                var start = 0;
                while (start < bytes.Length)
                {
                    var index = bytes.AsSpan(start).IndexOf("global-metadata.dat\0"u8);
                    if (index < 0)
                        break;
                    names.Add(section.VirtualStart + (ulong)(offset + start + index));
                    start += index + 20;
                }
            }
        }
        List<(ulong Start, ulong End, ulong Global)> results = [];
        if (names.Count == 0)
            return results;
        foreach (var section in image.GetSections().Where(static section => section.IsExec))
        {
            for (var offset = 0; offset < section.ImageLength; offset += BlockSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var bytes = image.ReadBytes(section.ImageStart + offset, Math.Min(BlockSize + 128, section.ImageLength - offset));
                var start = 0;
                while (start < Math.Min(BlockSize, bytes.Length))
                {
                    var index = bytes.AsSpan(start).IndexOf((ReadOnlySpan<byte>)[0x48, 0x8D, 0x0D]);
                    if (index < 0)
                        break;
                    index += start;
                    start = index + 3;
                    var decoder = Decoder.Create(64, new ByteArrayCodeReader(bytes, index, bytes.Length - index));
                    decoder.IP = section.VirtualStart + (ulong)(offset + index);
                    Instruction name = decoder.Decode();
                    Instruction load = decoder.Decode();
                    Instruction store = decoder.Decode();
                    if (
                        !names.Contains(name.IPRelativeMemoryAddress)
                        || load.Code != Code.Call_rel32_64
                        || store.Code != Code.Mov_rm64_r64
                        || store.Op1Register != Register.RAX
                        || !store.IsIPRelativeMemoryOperand
                    )
                        continue;
                    for (int count = 0; count < 12; count++)
                    {
                        Instruction call = decoder.Decode();
                        if (call.IsInvalid || call.Mnemonic == Mnemonic.Ret)
                            break;
                        if (call.Code != Code.Call_rel32_64)
                            continue;
                        Instruction test = decoder.Decode();
                        ulong target = call.NearBranchTarget;
                        if (
                            test.Code == Code.Test_rm8_r8
                            && test.Op0Register == Register.AL
                            && test.Op1Register == Register.AL
                            && image.GetSections().Any(s => s.IsExec && target >= s.VirtualStart && target + 64 <= s.VirtualEnd)
                            && image.ReadMappedBytes(target, 64).AsSpan().IndexOf((ReadOnlySpan<byte>)[0x81, 0x39, 0xDA, 0xFE, 0x57, 0x13]) >= 0
                            && image
                                .GetSections()
                                .Any(s =>
                                    s.IsData && store.IPRelativeMemoryAddress >= s.VirtualStart && store.IPRelativeMemoryAddress + 8 <= s.VirtualEnd
                                )
                        )
                            results.Add((store.IP, call.NextIP, store.IPRelativeMemoryAddress));
                        break;
                    }
                }
            }
        }
        return [.. results.Distinct()];
    }

    internal static byte[] Recover(IFileFormatStream image, byte[] metadata, LoadOptions options)
    {
        if (image.Arch != "x64")
            throw new NotSupportedException("Azur Promilia metadata requires a Windows x64 image.");
        if (
            metadata.Length < PayloadOffset + 8
            || metadata.Length > MaximumMetadataSize
            || BinaryPrimitives.ReadUInt32LittleEndian(metadata) != 0x1357FEDA
        )
            throw new InvalidDataException("Invalid Azur Promilia Code Philosophy metadata header.");
        uint payloadSize = BinaryPrimitives.ReadUInt32LittleEndian(metadata.AsSpan(4));
        if (payloadSize < 0x108 || payloadSize != metadata.Length - PayloadOffset)
            throw new InvalidDataException("Azur Promilia encrypted metadata payload length does not match the file.");
        var initializers = FindInitializer(image, options?.CancellationToken ?? default);
        if (initializers.Count != 1)
            throw new NotSupportedException($"Expected one Azur Promilia metadata initializer, found {initializers.Count}.");
        var (start, end, global) = initializers[0];
        using Unicorn emulator = new(Common.UC_ARCH_X86, Common.UC_MODE_64);
        try
        {
            options?.ProgressCallback?.Invoke(new("Map Azur Promilia image", 0, 1));
            foreach (var section in image.GetSections())
            {
                long pageStart = (long)(section.VirtualStart & ~0xFFFUL);
                long pageEnd = (long)((section.VirtualEnd + 0x1000) & ~0xFFFUL);
                emulator.MemMap(
                    pageStart,
                    (ulong)(pageEnd - pageStart),
                    Common.UC_PROT_READ | (section.IsExec ? Common.UC_PROT_EXEC : Common.UC_PROT_WRITE)
                );
                if (!section.IsBSS)
                {
                    for (var offset = 0; offset < section.ImageLength; offset += BlockSize)
                    {
                        options?.CancellationToken.ThrowIfCancellationRequested();
                        emulator.MemWrite(
                            (long)section.VirtualStart + offset,
                            image.ReadBytes((long)section.ImageStart + offset, Math.Min(BlockSize, section.ImageLength - offset))
                        );
                    }
                }
            }
            emulator.MemMap(StackAddress, StackSize, Common.UC_PROT_READ | Common.UC_PROT_WRITE);
            emulator.MemMap(MetadataAddress, (ulong)(metadata.Length + 0xFFF) & ~0xFFFUL, Common.UC_PROT_READ | Common.UC_PROT_WRITE);
            emulator.MemWrite(MetadataAddress, metadata);
            // Resume immediately after MetadataLoader::LoadMetadataFile with its mapped-file result in RAX.
            emulator.RegWrite(X86.UC_X86_REG_RAX, MetadataAddress);
            emulator.RegWrite(X86.UC_X86_REG_RSP, StackAddress + StackSize - 0x100);
            emulator.RegWrite(X86.UC_X86_REG_CR4, 0x600);
            options?.ProgressCallback?.Invoke(new("Map Azur Promilia image", 1, 1));
            options?.CancellationToken.ThrowIfCancellationRequested();
            options?.ProgressCallback?.Invoke(new("Emulate Azur Promilia metadata decoder", 0, 1));
            using var cancellation = (options?.CancellationToken ?? default).Register(emulator.EmuStop);
            emulator.EmuStart((long)start, (long)end, 120000000, 0);
            options?.CancellationToken.ThrowIfCancellationRequested();
            if ((ulong)emulator.RegRead(X86.UC_X86_REG_RIP) != end)
                throw new InvalidDataException($"Azur Promilia metadata emulation did not reach 0x{end:X}.");
            if ((emulator.RegRead(X86.UC_X86_REG_RAX) & 0xFF) == 0)
                throw new InvalidDataException("Azur Promilia metadata decoder rejected the CODEPHIL signature.");
            byte[] pointerBytes = new byte[8];
            emulator.MemRead((long)global, pointerBytes);
            long pointer = BinaryPrimitives.ReadInt64LittleEndian(pointerBytes);
            if (pointer != MetadataAddress)
                throw new InvalidDataException("Azur Promilia metadata initializer returned an unexpected mapped-file pointer.");
            byte[] payload = new byte[payloadSize];
            emulator.MemRead(pointer + PayloadOffset, payload);
            if (!payload.AsSpan(0, 8).SequenceEqual("CODEPHIL"u8))
                throw new InvalidDataException("Azur Promilia decoded metadata has an invalid CODEPHIL signature.");
            ReadOnlySpan<byte> decoded = payload.AsSpan(8);
            if (BinaryPrimitives.ReadUInt32LittleEndian(decoded) != 0xFAB11BAF)
                throw new InvalidDataException("Azur Promilia decoded metadata has an invalid IL2CPP magic.");
            if (BinaryPrimitives.ReadInt32LittleEndian(decoded[4..]) != 31 || BinaryPrimitives.ReadInt32LittleEndian(decoded[8..]) != 0x100)
                throw new NotSupportedException("Azur Promilia decoded metadata does not use the supported version 31 header.");
            for (int offset = 8; offset < 0x100; offset += 8)
            {
                int tableOffset = BinaryPrimitives.ReadInt32LittleEndian(decoded[offset..]);
                int size = BinaryPrimitives.ReadInt32LittleEndian(decoded[(offset + 4)..]);
                if (tableOffset < 0 || size < 0 || tableOffset > decoded.Length - size)
                    throw new InvalidDataException($"Azur Promilia metadata table at 0x{offset:X} exceeds the decoded file.");
            }
            options?.ProgressCallback?.Invoke(new("Emulate Azur Promilia metadata decoder", 1, 1));
            return decoded.ToArray();
        }
        catch (UnicornEngineException error)
        {
            throw new InvalidDataException(
                $"Azur Promilia metadata emulation failed at 0x{emulator.RegRead(X86.UC_X86_REG_RIP):X}: {error.Message}",
                error
            );
        }
        finally
        {
            emulator.Close();
        }
    }
}
