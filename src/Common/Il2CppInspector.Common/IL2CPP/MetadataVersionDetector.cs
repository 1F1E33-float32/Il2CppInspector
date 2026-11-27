using System.Buffers.Binary;
using System.Collections.Immutable;
using Il2CppInspector.Cpp.UnityHeaders;
using Il2CppInspector.Next.Metadata;
using VersionedSerialization;

namespace Il2CppInspector;

internal static partial class MetadataVersionDetector
{
    // Derive candidates from the shipped final/patch layouts, including both header lengths used by metadata 24.
    private static readonly (int Version, int HeaderSize)[] Layouts = UnityHeaders
        .GetAllTypeHeaders()
        .Select(static resource => UnityHeaders.GetHeadersForVersion(resource.VersionRange.Min).MetadataVersion)
        .Select(static version => (version.Major, Il2CppGlobalMetadataHeader.StructSize(version)))
        .Distinct()
        .OrderBy(static layout => layout.Major)
        .ToArray();

    internal static ImmutableArray<int> Detect(Stream binary, ReadOnlySpan<byte> metadata, LoadOptions options)
    {
        if (!binary.CanRead || !binary.CanSeek)
            throw new ArgumentException("Metadata version detection requires a readable, seekable binary stream.", nameof(binary));
        if (metadata.Length < 12 || ReadInt(metadata, 0) != Il2CppGlobalMetadataHeader.ExpectedSanity)
            throw new InvalidDataException("Metadata version detection requires an intact global-metadata.dat header with sanity 0xFAB11BAF.");

        var cancellation = options?.CancellationToken ?? default;
        cancellation.ThrowIfCancellationRequested();
        var position = binary.Position;
        var matches = ImmutableArray.CreateBuilder<int>();
        var headerSize = ReadInt(metadata, 8);
        var candidates = Layouts.Where(layout => layout.HeaderSize == headerSize).ToArray();
        if (candidates.Length == 0)
            return [];
        var copy = metadata.ToArray();
        try
        {
            foreach (var layout in candidates)
            {
                cancellation.ThrowIfCancellationRequested();
                if (!ValidateSections(metadata, layout.Version, headerSize))
                    continue;
                BinaryPrimitives.WriteInt32LittleEndian(copy.AsSpan(4), layout.Version);
                try
                {
                    using var log = new AnalysisLog(null, cancellation);
                    using var input = new MemoryStream(copy, writable: false);
                    using var candidate = Metadata.FromStream(input);
                    if (!ValidateMetadata(candidate))
                        throw new InvalidDataException("Metadata record sizes, indices or ownership ranges do not match.");

                    // Reload per candidate: relocations, alternate loading strategies and registration scanning may mutate an image.
                    var loaded = FileFormatStream.Load(binary, options);
                    if (loaded is null)
                        throw new NotSupportedException("Metadata version detection requires a supported executable file format.");
                    var images = loaded.Images.ToArray();
                    try
                    {
                        var valid = images.Length > 0;
                        foreach (var image in images)
                        {
                            cancellation.ThrowIfCancellationRequested();
                            var decoded = Il2CppBinary.LoadForMetadataVersionDetection(image, candidate);
                            if (decoded is null || !ValidateBinary(candidate, decoded))
                            {
                                valid = false;
                                break;
                            }
                        }
                        if (valid)
                            matches.Add(layout.Version);
                    }
                    finally
                    {
                        foreach (var image in images.Distinct())
                            if (!ReferenceEquals(image, loaded))
                                ((IDisposable)image).Dispose();
                        ((IDisposable)loaded).Dispose();
                    }
                }
                catch (Exception ex)
                    when (ex
                            is InvalidDataException
                                or EndOfStreamException
                                or NotSupportedException
                                or InvalidOperationException
                                or ArgumentException
                                or IndexOutOfRangeException
                                or KeyNotFoundException
                                or OverflowException
                                or FormatException
                    )
                {
                    // A trial layout can fail anywhere in a stock reader. Cancellation and I/O failures still reach the plugin.
                    cancellation.ThrowIfCancellationRequested();
                    AnalysisLog.WriteLine($"Metadata version candidate {layout.Version} rejected: {ex.Message}");
                }
            }
        }
        finally
        {
            binary.Position = position;
        }
        return matches.ToImmutable();
    }

    private static int ReadInt(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadInt32LittleEndian(bytes[offset..]);

    private static bool ValidateSections(ReadOnlySpan<byte> bytes, int version, int headerSize)
    {
        if (headerSize > bytes.Length)
            return false;
        var stride = version >= 39 ? 12 : 8;
        List<(int Start, int End)> ranges = [];
        for (var field = 8; field < headerSize; field += stride)
        {
            var start = ReadInt(bytes, field);
            var size = ReadInt(bytes, field + 4);
            if (start < 0 || size < 0 || (long)start + size > bytes.Length || (size != 0 && start < headerSize))
                return false;
            if (stride == 12)
            {
                var count = ReadInt(bytes, field + 8);
                if (count < 0 || count > size || (count == 0) != (size == 0))
                    return false;
            }
            if (size != 0)
                ranges.Add((start, start + size));
        }
        ranges.Sort(static (a, b) => a.Start.CompareTo(b.Start));
        if (ranges.Zip(ranges.Skip(1)).Any(static pair => pair.First.End > pair.Second.Start))
            return false;

        // Check literal lengths before the stock reader allocates strings from them.
        var literalsOffset = ReadInt(bytes, 8);
        var literalsSize = ReadInt(bytes, 12);
        var literalDataSize = ReadInt(bytes, 8 + stride + 4);
        var literalWidth = version >= 39 ? 4 : 8;
        if (literalsSize % literalWidth != 0 || (version >= 39 && literalsSize == 0))
            return false;
        var previous = 0;
        for (var i = 0; i < literalsSize; i += literalWidth)
        {
            var length = version >= 39 ? 0 : ReadInt(bytes, literalsOffset + i);
            var start = ReadInt(bytes, literalsOffset + i + (version >= 39 ? 0 : 4));
            if (length < 0 || start < 0 || (long)start + length > literalDataSize || (version >= 39 && start < previous))
                return false;
            previous = start;
        }
        var stringsOffset = ReadInt(bytes, 8 + stride * 2);
        var stringsSize = ReadInt(bytes, 12 + stride * 2);
        return stringsSize > 0 && bytes[stringsOffset + stringsSize - 1] == 0;
    }
}
