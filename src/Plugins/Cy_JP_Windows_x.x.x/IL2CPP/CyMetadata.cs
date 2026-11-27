using System.Buffers.Binary;
using Iced.Intel;
using UnicornEngine;
using UnicornEngine.Const;

namespace Il2CppInspector.Plugin.Cy;

internal static class CyMetadata
{
    private const long StackAddress = 0x700000000000;
    private const long StackSize = 0x100000;
    private const long ReturnAddress = StackAddress + StackSize;
    private const int MaximumMetadataSize = 256 * 1024 * 1024;

    // MetadataCache::Initialize loads the filename, calls its constructor and stores RAX in s_GlobalMetadata.
    // The constructor wrappers at 0x1803EDFB0 and 0x1803EE6E0 call a cached SIMD decoder.
    internal static List<(ulong Constructor, ulong Global)> FindConstructors(IFileFormatStream image, CancellationToken cancellationToken = default)
    {
        HashSet<ulong> strings = [];
        foreach (Section section in image.GetSections().Where(static section => !section.IsExec && !section.IsBSS))
        {
            const int blockSize = 1024 * 1024;
            for (int offset = 0; offset < section.ImageLength; offset += blockSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                byte[] bytes = image.ReadBytes(section.ImageStart + offset, Math.Min(blockSize + 19, section.ImageLength - offset));
                int start = 0;
                while (start < bytes.Length)
                {
                    int index = bytes.AsSpan(start).IndexOf("global-metadata.dat\0"u8);
                    if (index < 0)
                        break;
                    strings.Add(section.VirtualStart + (ulong)(offset + start + index));
                    start += index + 20;
                }
            }
        }
        ReadOnlySpan<byte> referenceInstruction = [0x48, 0x8D, 0x0D];
        List<(ulong Constructor, ulong Global)> constructors = [];
        if (strings.Count == 0)
            return constructors;
        foreach (Section section in image.GetSections().Where(static section => section.IsExec))
        {
            const int blockSize = 1024 * 1024;
            for (int offset = 0; offset < section.ImageLength; offset += blockSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                byte[] bytes = image.ReadBytes(section.ImageStart + offset, Math.Min(blockSize + 32, section.ImageLength - offset));
                int start = 0;
                while (start < Math.Min(blockSize, bytes.Length))
                {
                    int index = bytes.AsSpan(start).IndexOf(referenceInstruction);
                    if (index < 0)
                        break;
                    index += start;
                    start = index + 3;
                    var decoder = Decoder.Create(64, new ByteArrayCodeReader(bytes, index, bytes.Length - index));
                    decoder.IP = section.VirtualStart + (ulong)(offset + index);
                    Instruction name = decoder.Decode();
                    Instruction call = decoder.Decode();
                    Instruction store = decoder.Decode();
                    if (
                        !strings.Contains(name.IPRelativeMemoryAddress)
                        || call.Code != Code.Call_rel32_64
                        || store.Code != Code.Mov_rm64_r64
                        || store.Op1Register != Register.RAX
                        || !store.IsIPRelativeMemoryOperand
                    )
                        continue;
                    ulong target = call.NearBranchTarget;
                    if (!image.TryMapVATR(target, out _))
                        continue;
                    var body = Decoder.Create(64, new ByteArrayCodeReader(image.ReadMappedBytes(target, 96)));
                    body.IP = target;
                    for (int instructionIndex = 0; instructionIndex < 16; instructionIndex++)
                    {
                        Instruction transfer = body.Decode();
                        if (transfer.Code is Code.Call_rel32_64 or Code.Jmp_rel32_64)
                        {
                            target = transfer.NearBranchTarget;
                            break;
                        }
                        if (transfer.Mnemonic == Mnemonic.Ret)
                            break;
                    }
                    if (!image.TryMapVATR(target, out _))
                        continue;
                    body = Decoder.Create(64, new ByteArrayCodeReader(image.ReadMappedBytes(target, 96)));
                    body.IP = target;
                    // The cache read and conditional return distinguish the embedded decoder from a file-loading routine.
                    for (int instructionIndex = 0; instructionIndex < 16; instructionIndex++)
                    {
                        Instruction cache = body.Decode();
                        if (cache.Code != Code.Mov_r64_rm64 || cache.Op0Register != Register.RAX || !cache.IsIPRelativeMemoryOperand)
                            continue;
                        Instruction test = body.Decode();
                        Instruction branch = body.Decode();
                        if (test.Mnemonic == Mnemonic.Test && branch.FlowControl == FlowControl.ConditionalBranch)
                            constructors.Add((call.NearBranchTarget, store.IPRelativeMemoryAddress));
                        break;
                    }
                }
            }
        }
        return [.. constructors.Distinct()];
    }

    internal static (byte[] Data, ulong Address) Recover(IFileFormatStream image, LoadOptions options)
    {
        if (image.Arch != "x64")
            throw new NotSupportedException("Cy embedded metadata requires a Windows x64 image.");
        var constructors = FindConstructors(image, options?.CancellationToken ?? default);
        if (constructors.Count != 1)
            throw new InvalidDataException($"Cy expected one metadata constructor, found {constructors.Count}.");
        (ulong constructor, ulong global) = constructors[0];
        options?.ProgressCallback?.Invoke(new("Map Cy image", 0, 1));
        using var emulator = new Unicorn(Common.UC_ARCH_X86, Common.UC_MODE_64);
        try
        {
            Section[] sections = [.. image.GetSections()];
            foreach (Section section in sections)
            {
                long start = (long)(section.VirtualStart & ~0xFFFUL);
                long end = (long)((section.VirtualEnd + 0x1000) & ~0xFFFUL);
                emulator.MemMap(start, (ulong)(end - start), Common.UC_PROT_READ | (section.IsExec ? Common.UC_PROT_EXEC : Common.UC_PROT_WRITE));
                if (!section.IsBSS)
                {
                    for (int offset = 0; offset < section.ImageLength; offset += 1024 * 1024)
                    {
                        options?.CancellationToken.ThrowIfCancellationRequested();
                        byte[] block = image.ReadBytes((long)section.ImageStart + offset, Math.Min(1024 * 1024, section.ImageLength - offset));
                        emulator.MemWrite((long)section.VirtualStart + offset, block);
                    }
                }
            }
            options?.ProgressCallback?.Invoke(new("Map Cy image", 1, 1));
            emulator.MemMap(ReturnAddress, 4096, Common.UC_PROT_ALL);
            emulator.MemWrite(ReturnAddress, [0xF4]);
            emulator.MemMap(StackAddress, StackSize, Common.UC_PROT_READ | Common.UC_PROT_WRITE);
            emulator.MemWrite(StackAddress + StackSize - 8, BitConverter.GetBytes(ReturnAddress));
            emulator.RegWrite(X86.UC_X86_REG_RSP, StackAddress + StackSize - 8);
            emulator.RegWrite(X86.UC_X86_REG_CR4, 0x600);
            options?.CancellationToken.ThrowIfCancellationRequested();
            options?.ProgressCallback?.Invoke(new("Emulate Cy metadata constructor", 0, 1));
            using CancellationTokenRegistration cancellation = (options?.CancellationToken ?? default).Register(emulator.EmuStop);
            try
            {
                emulator.EmuStart((long)constructor, ReturnAddress, 120000000, 0);
                options?.CancellationToken.ThrowIfCancellationRequested();
                if (emulator.RegRead(X86.UC_X86_REG_RIP) != ReturnAddress)
                    throw new InvalidDataException($"Cy metadata constructor at 0x{constructor:X} did not reach its return breakpoint.");
            }
            catch (UnicornEngineException error)
            {
                throw new InvalidDataException(
                    $"Cy metadata constructor at 0x{constructor:X} failed at 0x{emulator.RegRead(X86.UC_X86_REG_RIP):X}.",
                    error
                );
            }
            options?.ProgressCallback?.Invoke(new("Emulate Cy metadata constructor", 1, 1));
            options?.ProgressCallback?.Invoke(new("Read Cy metadata", 0, 1));
            ulong pointer = (ulong)emulator.RegRead(X86.UC_X86_REG_RAX);
            byte[] header = new byte[16];
            emulator.MemRead((long)pointer, header);
            int headerSize = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(8));
            if (headerSize < 16 || headerSize > 4096 || headerSize % 4 != 0)
                throw new InvalidDataException($"Cy metadata at 0x{pointer:X} has invalid header size {headerSize}.");
            header = new byte[headerSize];
            emulator.MemRead((long)pointer, header);
            // Stock headers use offset/size pairs or offset/size/count triples. Keep every bounded interpretation
            // until the plugin can identify the version against the recovered binary; the version word is not evidence.
            var length = 0;
            foreach (var stride in new[] { 8, 12 })
            {
                if ((headerSize - 8) % stride != 0)
                    continue;
                var end = headerSize;
                for (var offset = 8; offset < headerSize; offset += stride)
                {
                    int start = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(offset));
                    int size = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(offset + 4));
                    if (start < 0 || size < 0 || start > MaximumMetadataSize - size || (size != 0 && start < headerSize))
                    {
                        end = 0;
                        break;
                    }
                    if (stride == 12)
                    {
                        int count = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(offset + 8));
                        if (count < 0 || count > size || (count == 0) != (size == 0))
                        {
                            end = 0;
                            break;
                        }
                    }
                    end = Math.Max(end, start + size);
                }
                if (
                    end > 0
                    && sections.Any(section =>
                        pointer >= section.VirtualStart && pointer <= section.VirtualEnd && (ulong)end <= section.VirtualEnd - pointer + 1
                    )
                )
                    length = Math.Max(length, end);
            }
            if (length == 0)
                throw new InvalidDataException($"Cy metadata at 0x{pointer:X} has no valid section ranges within its PE section.");
            byte[] decoded = new byte[length];
            emulator.MemRead((long)pointer, decoded);
            // MetadataCache ignores the first two header words. Restore the signature; the plugin probes the version separately.
            BinaryPrimitives.WriteUInt32LittleEndian(decoded, 0xFAB11BAF);
            // Preserve the constructor's global result for callers reading the binary's native metadata pointers.
            image.Write(image.MapVATR(pointer), decoded);
            image.Write(image.MapVATR(global), BitConverter.GetBytes(pointer));
            options?.ProgressCallback?.Invoke(new("Read Cy metadata", 1, 1));
            return (decoded, pointer);
        }
        catch (UnicornEngineException error)
        {
            throw new InvalidDataException($"Cy metadata emulation at 0x{constructor:X} failed: {error.Message}", error);
        }
        finally
        {
            emulator.Close();
        }
    }
}
