using System.Collections.Immutable;
using Il2CppInspector.Cpp.UnityHeaders;
using Il2CppInspector.Model;
using Il2CppInspector.Reflection;

namespace Il2CppInspector.Plugins
{
    /// <summary>Files consumed by a plugin. Inputs outside this set are ignored.</summary>
    [Flags]
    public enum GamePluginInputs
    {
        Binary = 1,
        Metadata = 2,
        StartupMetadata = 4,
    }

    public abstract class GamePlugin
    {
        public abstract string GameId { get; }

        /// <summary>Required files, including startup metadata that can be found beside the metadata file.</summary>
        public virtual GamePluginInputs RequiredInputs => GamePluginInputs.Binary | GamePluginInputs.Metadata;
        public virtual string StartupMetadataFileName => null;
        public virtual bool StreamExports => false;
        public virtual UnityVersion DefaultUnityVersion => null;
        public virtual string PythonMetadataProcessor => "";
        public virtual string RegistrationSignature => null;

        /// <summary>
        /// Detects final/patch metadata header versions using a normalized metadata file and its binary.
        /// Ignores the existing version word, preserves both inputs and the binary stream position, and returns
        /// all validated candidates in ascending order. An empty result means no supported layout matched;
        /// multiple results (such as 106 and 107) require the plugin to resolve the ambiguity.
        /// This opt-in operation is never used by normal plugin selection or stock loading.
        /// </summary>
        protected static ImmutableArray<int> DetectMetadataVersions(Stream binary, ReadOnlySpan<byte> metadata, LoadOptions options = null) =>
            MetadataVersionDetector.Detect(binary, metadata, options);

        public abstract bool Matches(ReadOnlySpan<byte> metadata);

        /// <summary>Matches a borrowed binary stream when metadata is embedded in the image.</summary>
        public virtual bool Matches(Stream binary, ReadOnlySpan<byte> metadata) => Matches(metadata);

        public abstract Il2CppInspector Load(
            Stream binary,
            byte[] metadata,
            byte[] startupMetadata,
            LoadOptions options,
            EventHandler<string> status
        );

        public virtual UnityHeaders GetHeaders() => throw new NotSupportedException($"{GameId} uses the stock IL2CPP pipeline.");

        public virtual IGameTypeLayouts CreateTypeLayouts(TypeModel model) =>
            throw new NotSupportedException($"{GameId} uses the stock IL2CPP pipeline.");

        public virtual GameNativeModel CreateNativeModel(TypeModel model) =>
            throw new NotSupportedException($"{GameId} uses the stock IL2CPP pipeline.");

        public virtual IGameAnalysisModel CreateAnalysisModel(AppModel model) =>
            throw new NotSupportedException($"{GameId} uses the stock IL2CPP pipeline.");
    }
}
