using Il2CppInspector.Cpp.UnityHeaders;
using Il2CppInspector.Model;
using Il2CppInspector.Plugins;
using Il2CppInspector.Reflection;

namespace Il2CppInspector.Plugin.ABC;

/// <summary>Loads ABC's MHY version 1 metadata and the Windows x64 runtime.</summary>
public sealed class AbcPlugin : GamePlugin
{
    public override string GameId => "ABC_CN_Windows_0.75.2";
    public override bool StreamExports => true;
    public override UnityVersion DefaultUnityVersion => new("2022.3.14f1");
    public override string PythonMetadataProcessor =>
        ResourceHelper.GetText(typeof(AbcPlugin).Assembly, "Il2CppInspector.Plugin.Morax.RuntimeCaches.py");

    // GlobalMetadata::Initialize (0x1807D0BF0) embeds this build's table ranges.
    public override bool Matches(ReadOnlySpan<byte> metadata) =>
        metadata.Length == 25249924
        && metadata[..16].SequenceEqual<byte>([0x4D, 0x48, 0x59, 0x01, 0x45, 0xB9, 0x10, 0x76, 0xF3, 0x7E, 0xFC, 0x6D, 0x62, 0x3E, 0x0B, 0x6B]);

    public override Il2CppInspector Load(Stream binary, byte[] metadata, byte[] startupMetadata, LoadOptions options, EventHandler<string> status)
    {
        var image = PEReader.Load(binary, options, status) ?? throw new InvalidDataException("Unsupported executable file format.");
        status?.Invoke(this, $"Decoding {GameId} metadata");
        var adapter = new AbcMetadata(image, metadata, this, status);
        return new Il2CppInspector(adapter.Binary, adapter.Metadata);
    }

    public override UnityHeaders GetHeaders() => AbcRuntimeHeaders.GetHeaders();

    public override IGameTypeLayouts CreateTypeLayouts(TypeModel model) =>
        // GetSharedType 0x1807A12D0 maps enums to the eight System.*Enum sharing types.
        new SharedTypeLayouts(model, ((AbcMetadata)model.Package.Metadata.GameAdapter).GenericLayoutGroups, true);

    public override GameNativeModel CreateNativeModel(TypeModel model) => new AbcNativeModel(model);

    public override IGameAnalysisModel CreateAnalysisModel(AppModel model)
    {
        ((AbcNativeModel)model.GameNativeModel).InitializeNaming(model);
        model.BuildMetadataDataUsages();
        return new NativeLayoutAnalysisModel(model);
    }
}
