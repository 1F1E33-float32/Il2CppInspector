namespace Il2CppInspector.Tests.Common.Plugins
{
    internal static class GamePluginsTests
    {
        internal static void Run()
        {
            foreach (var plugin in GamePlugins.Installed)
            {
                Check(
                    ReferenceEquals(GamePlugins.Get(plugin.GameId.ToLowerInvariant()), plugin),
                    "Game selection remains case-insensitive: " + plugin.GameId
                );
                using var truncatedBinary = new MemoryStream("MZ"u8.ToArray());
                Check(
                    !plugin.Matches([]) && !plugin.Matches("stock"u8) && !plugin.Matches(Stream.Null, []) && !plugin.Matches(truncatedBinary, []),
                    "Plugin rejects truncated and foreign inputs: " + plugin.GameId
                );
                if (plugin.StreamExports)
                {
                    _ = CppTypeCollection.FromUnityHeaders(plugin.GetHeaders());
                    Pass("Plugin runtime headers parse with the stock header resources: " + plugin.GameId);
                }
            }
            Plugin.GameSampleTests.RunInstalled();
        }
    }
}
