using System.Buffers.Binary;
using Il2CppInspector.Plugins;

namespace Il2CppInspector.Plugin.AzurPromilia;

/// <summary>Recovers Code Philosophy metadata using the decoder in the supplied Windows binary.</summary>
public sealed class AzurPromiliaPlugin : GamePlugin
{
    public override string GameId => "AzurPromilia_xx_Windows_x.x.x";

    public override bool Matches(ReadOnlySpan<byte> metadata) =>
        metadata.Length >= 0x150 && BinaryPrimitives.ReadUInt32LittleEndian(metadata) == 0x1357FEDA;

    public override bool Matches(Stream binary, ReadOnlySpan<byte> metadata)
    {
        if (!Matches(metadata) || binary.Length < 0x40)
            return false;
        binary.Position = 0;
        if (binary.ReadByte() != 'M' || binary.ReadByte() != 'Z')
            return false;
        binary.Position = 0;
        var image = FileFormatStream.Load(binary);
        using var owner = (IDisposable)image;
        return image is { Arch: "x64" } && AzurPromiliaMetadata.FindInitializer(image).Count == 1;
    }

    public override Il2CppInspector Load(Stream binary, byte[] metadata, byte[] startupMetadata, LoadOptions options, EventHandler<string> status)
    {
        var image =
            FileFormatStream.Load(binary, options, status) ?? throw new InvalidDataException("Azur Promilia requires a Windows x64 PE image.");
        try
        {
            status?.Invoke(this, "Decrypting Azur Promilia metadata");
            using MemoryStream stream = new(AzurPromiliaMetadata.Recover(image, metadata, options));
            var decoded = Metadata.FromStream(stream, status);
            try
            {
                var native =
                    Il2CppBinary.Load(image, decoded, status)
                    ?? throw new InvalidDataException("Could not locate Azur Promilia IL2CPP registration structures.");
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
