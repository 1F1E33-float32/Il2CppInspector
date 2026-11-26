using System.Collections.Frozen;
using System.IO.Hashing;
using System.Text;

namespace Il2CppInspector.Plugin.ABC;

internal static class AbcNames
{
    private static readonly FrozenDictionary<ulong, string> KnownNames = LoadKnownNames();

    internal static string DecodeIdentifier(ulong identifier) => KnownNames.GetValueOrDefault(identifier, identifier.ToString("X16"));

    private static FrozenDictionary<ulong, string> LoadKnownNames()
    {
        // Names from Unity 2022.3.14f1 managed modules matched against this build's metadata,
        // together with the CLR identities requested by Runtime::Init (0x1807D48E0).
        using Stream stream =
            typeof(AbcNames).Assembly.GetManifestResourceStream("Il2CppInspector.Plugin.ABC.EngineNames.txt")
            ?? throw new InvalidDataException("ABC engine name resource is missing.");
        using StreamReader reader = new(stream, new UTF8Encoding(false, true), false);
        Dictionary<ulong, string> names = [];
        while (reader.ReadLine() is { } name)
        {
            if (name.StartsWith(';'))
                continue;
            if (name.Length == 0)
            {
                throw new InvalidDataException("ABC engine name resource contains an empty entry.");
            }
            // 0x1807D32D0 is seeded XXH3 with an identifier tag. The long-input routines
            // at 0x1807DE490/0x1807DDE80 use the same seed and standard XXH3 secret.
            ulong identifier = XxHash3.HashToUInt64(Encoding.UTF8.GetBytes(name), 0x2B04E834E39A39F4) | 0xA000000000000000;
            if (!names.TryAdd(identifier, name))
            {
                throw new InvalidDataException($"ABC engine names '{names[identifier]}' and '{name}' share identifier {identifier:X16}.");
            }
        }
        return names.ToFrozenDictionary();
    }
}
