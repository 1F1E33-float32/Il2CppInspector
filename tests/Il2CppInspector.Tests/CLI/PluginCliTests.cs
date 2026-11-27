namespace Il2CppInspector.Tests.CLI
{
    internal static class PluginCliTests
    {
        private const string MissingGame = "NONE_CN_Windows_0.0.0";

        internal static void Run(string[] args)
        {
            var cli = Path.GetFullPath(args[1]);
            var directory = Path.GetDirectoryName(cli);
            var pluginDirectory = Path.Combine(directory, "plugins");
            Check(
                Directory
                    .EnumerateFiles(pluginDirectory, "Il2CppInspector.Plugin.*.dll")
                    .Select(static path => Path.GetFileName(path)["Il2CppInspector.Plugin.".Length..^4])
                    .Order(StringComparer.Ordinal)
                    .SequenceEqual(GamePlugins.Installed.Select(static plugin => plugin.GameId).Order(StringComparer.Ordinal)),
                "Every installed plugin is packaged directly in plugins/"
            );
            Check(!Directory.EnumerateDirectories(pluginDirectory).Any(), "Plugin dependencies share a flat directory");
            Check(
                !Directory
                    .EnumerateFiles(directory, "*.dll")
                    .Select(Path.GetFileName)
                    .Intersect(Directory.EnumerateFiles(pluginDirectory, "*.dll").Select(Path.GetFileName), StringComparer.OrdinalIgnoreCase)
                    .Any(),
                "Host dependencies are not copied into plugins/"
            );
            var nativeFiles = Directory.EnumerateFiles(Path.Combine(directory, "runtimes"), "*", SearchOption.AllDirectories).ToArray();
            Check(
                nativeFiles.Length > 0
                    && nativeFiles.All(path =>
                    {
                        var relative = Path.GetRelativePath(Path.Combine(directory, "runtimes"), path).Split(Path.DirectorySeparatorChar);
                        return relative.Length == 3
                            && relative[0] is "win-x64" or "win-arm64" or "linux-x64" or "linux-arm64" or "osx-x64" or "osx-arm64"
                            && relative[1] == "native";
                    }),
                "Native dependencies use runtimes/<supported-rid>/native/"
            );
            var available = RunCli(cli);
            Check(
                GamePlugins.Installed.All(p => available.Contains(p.GameId)),
                "Published CLI discovers every game DLL through the plugin loader library"
            );
            var (listExitCode, listed, _) = TestEnvironment.RunProcess("dotnet", [cli, "--list-games"]);
            Check(
                listExitCode == 0
                    && listed
                        .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Order(StringComparer.Ordinal)
                        .SequenceEqual(GamePlugins.Installed.Select(static p => p.GameId).Order(StringComparer.Ordinal)),
                "--list-games prints every installed GameId once"
            );
            var (jsonListExitCode, jsonListed, _) = TestEnvironment.RunProcess("dotnet", [cli, "--list-games", "--json-progress"]);
            var expectedInputs = GamePlugins.Installed.ToDictionary(
                static p => p.GameId,
                static p =>
                    (
                        p.RequiredInputs.HasFlag(GamePluginInputs.Binary),
                        p.RequiredInputs.HasFlag(GamePluginInputs.Metadata),
                        p.RequiredInputs.HasFlag(GamePluginInputs.StartupMetadata)
                    ),
                StringComparer.Ordinal
            );
            var actualInputs = jsonListed
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line).RootElement)
                .ToDictionary(
                    static root => root.GetProperty("gameId").GetString(),
                    static root =>
                    {
                        var inputs = root.GetProperty("inputs");
                        return (
                            inputs.GetProperty("binary").GetBoolean(),
                            inputs.GetProperty("metadata").GetBoolean(),
                            inputs.GetProperty("startupMetadata").GetBoolean()
                        );
                    },
                    StringComparer.Ordinal
                );
            Check(
                jsonListExitCode == 0
                    && actualInputs.Count == expectedInputs.Count
                    && expectedInputs.All(pair => actualInputs.TryGetValue(pair.Key, out var value) && value == pair.Value),
                "--list-games --json-progress reports every plugin's RequiredInputs"
            );
            var isolated = Path.GetFullPath(args[2]);
            Directory.CreateDirectory(isolated);
            foreach (var file in Directory.EnumerateFiles(Path.GetDirectoryName(cli)))
            {
                if (!Path.GetFileName(file).StartsWith("Il2CppInspector.Plugin.", StringComparison.Ordinal))
                {
                    File.Copy(file, Path.Combine(isolated, Path.GetFileName(file)), true);
                }
            }
            var missing = RunCli(Path.Combine(isolated, Path.GetFileName(cli)));
            Check(
                missing.Contains("No installed plugin supports " + MissingGame)
                    && missing.TrimEnd().EndsWith("Available games:", StringComparison.Ordinal),
                "CLI without game DLLs reports the missing plugin clearly"
            );

            static string RunCli(string cli)
            {
                var (exitCode, output, errors) = TestEnvironment.RunProcess(
                    "dotnet",
                    [cli, "--game", MissingGame, "-i", "unused.bin", "-m", "unused.dat"]
                );
                if (exitCode != 1)
                    throw new InvalidOperationException($"CLI returned unexpected exit code {exitCode}.");
                return output + errors;
            }
        }
    }
}
