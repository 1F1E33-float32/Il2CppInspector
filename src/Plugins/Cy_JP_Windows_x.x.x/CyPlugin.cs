using System.Buffers.Binary;
using Il2CppInspector.Plugins;

namespace Il2CppInspector.Plugin.Cy;

/// <summary>Recovers embedded Windows metadata by emulating its binary's metadata constructor.</summary>
public sealed class CyPlugin : GamePlugin
{
    public override string GameId => "Cy_JP_Windows_x.x.x";
    public override GamePluginInputs RequiredInputs => GamePluginInputs.Binary;

    public override bool Matches(ReadOnlySpan<byte> metadata) => false;

    public override bool Matches(Stream binary, ReadOnlySpan<byte> metadata)
    {
        if (!metadata.IsEmpty || binary.Length < 0x40)
            return false;
        binary.Position = 0;
        if (binary.ReadByte() != 'M' || binary.ReadByte() != 'Z')
            return false;
        binary.Position = 0;
        var image = FileFormatStream.Load(binary);
        using var owner = (IDisposable)image;
        return image is { Arch: "x64" } && CyMetadata.FindConstructors(image).Count == 1;
    }

    public override Il2CppInspector Load(Stream binary, byte[] metadata, byte[] startupMetadata, LoadOptions options, EventHandler<string> status)
    {
        var image = FileFormatStream.Load(binary, options, status) ?? throw new InvalidDataException("Cy requires a Windows x64 PE image.");
        try
        {
            status?.Invoke(this, "Recovering Cy embedded metadata");
            var (data, address) = CyMetadata.Recover(image, options);
            status?.Invoke(this, "Detecting Cy metadata version");
            var versions = DetectMetadataVersions((Stream)image, data, options);
            if (versions.Length == 0)
                throw new InvalidDataException("No supported metadata version matches the recovered Cy metadata and GameAssembly.dll.");
            if (versions.Length != 1)
                throw new NotSupportedException($"Cy metadata version is ambiguous; matching versions: {string.Join(", ", versions)}.");
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), versions[0]);
            image.Write(image.MapVATR(address) + 4, versions[0]);
            status?.Invoke(this, $"Detected Cy metadata version {versions[0]}");
            using var stream = new MemoryStream(data);
            var decoded = Metadata.FromStream(stream, status);
            try
            {
                var native =
                    Il2CppBinary.Load(image, decoded, status)
                    ?? throw new InvalidDataException("Could not locate Cy IL2CPP registration structures in GameAssembly.dll.");
                return new Il2CppInspector(native, decoded);
            }
            catch
            {
                decoded.Dispose();
                throw;
            }
        }
        catch
        {
            ((IDisposable)image).Dispose();
            throw;
        }
    }
}
