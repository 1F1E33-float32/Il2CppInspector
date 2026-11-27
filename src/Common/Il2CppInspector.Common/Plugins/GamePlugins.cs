using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using McMaster.NETCore.Plugins;

namespace Il2CppInspector.Plugins
{
    public static class GamePlugins
    {
        private static readonly Lazy<IReadOnlyList<GamePlugin>> plugins = new(Discover);
        private static readonly List<PluginLoader> loaders = [];
        public static IReadOnlyList<GamePlugin> Installed => plugins.Value;

        public static GamePlugin Get(string gameId)
        {
            var parts = gameId?.Split('_');
            if (parts == null || parts.Length != 4 || parts.Any(string.IsNullOrWhiteSpace))
            {
                throw new ArgumentException($"Available games: {string.Join(", ", Installed.Select(p => p.GameId))}");
            }

            return Installed.SingleOrDefault(p => string.Equals(p.GameId, gameId, StringComparison.OrdinalIgnoreCase))
                ?? throw new NotSupportedException(
                    $"No installed plugin supports {gameId}. Available games: {string.Join(", ", Installed.Select(p => p.GameId))}"
                );
        }

        internal static GamePlugin Select(string gameId, ReadOnlySpan<byte> metadata, Stream binary = null)
        {
            if (gameId != null)
            {
                var selected = Get(gameId);
                if (!Matches(selected, binary, metadata))
                {
                    throw new InvalidDataException($"Metadata does not match game plugin {selected.GameId}.");
                }

                return selected;
            }
            List<GamePlugin> matches = [];
            foreach (var plugin in Installed)
            {
                if (Matches(plugin, binary, metadata))
                {
                    matches.Add(plugin);
                }
            }

            return matches.Count switch
            {
                0 => null,
                1 => matches[0],
                _ => throw new InvalidOperationException(
                    $"Multiple game plugins match this metadata ({string.Join(", ", matches.Select(static p => p.GameId))}); specify --game <GameId>."
                ),
            };
        }

        private static bool Matches(GamePlugin plugin, Stream binary, ReadOnlySpan<byte> metadata)
        {
            if (!plugin.RequiredInputs.HasFlag(GamePluginInputs.Metadata))
                metadata = [];
            if (binary == null)
                return plugin.Matches(metadata);
            long position = binary.Position;
            try
            {
                binary.Position = 0;
                return plugin.Matches(binary, metadata);
            }
            finally
            {
                binary.Position = position;
            }
        }

        private static IReadOnlyList<GamePlugin> Discover()
        {
            AssemblyLoadContext.Default.ResolvingUnmanagedDll += ResolveNativeLibrary;
            var assemblies = new HashSet<Assembly>(
                AppDomain
                    .CurrentDomain.GetAssemblies()
                    .Where(a => !a.IsDynamic && a.GetName().Name.StartsWith("Il2CppInspector.Plugin.", StringComparison.Ordinal))
            );
            foreach (var directory in new[] { AppContext.BaseDirectory, Path.Combine(AppContext.BaseDirectory, "plugins") })
            {
                if (!Directory.Exists(directory))
                {
                    continue;
                }

                var search = directory == AppContext.BaseDirectory ? SearchOption.TopDirectoryOnly : SearchOption.AllDirectories;
                foreach (
                    var path in Directory.EnumerateFiles(directory, "Il2CppInspector.Plugin.*.dll", search).OrderBy(p => p, StringComparer.Ordinal)
                )
                {
                    var name = AssemblyName.GetAssemblyName(path);
                    var assembly = assemblies.FirstOrDefault(a => AssemblyName.ReferenceMatchesDefinition(a.GetName(), name));
                    if (assembly == null)
                    {
                        var loader = PluginLoader.CreateFromAssemblyFile(
                            Path.GetFullPath(path),
                            sharedTypes: [typeof(GamePlugin)],
                            configure: config => config.PreferSharedTypes = true
                        );
                        loader.LoadContext.ResolvingUnmanagedDll += ResolveNativeLibrary;
                        loaders.Add(loader);
                        assembly = loader.LoadDefaultAssembly();
                    }
                    assemblies.Add(assembly);
                }
            }
            var result = assemblies
                .SelectMany(a => a.GetExportedTypes())
                .Where(t => !t.IsAbstract && typeof(GamePlugin).IsAssignableFrom(t))
                .Select(t => (GamePlugin)Activator.CreateInstance(t))
                .OrderBy(p => p.GameId, StringComparer.Ordinal)
                .ToArray();
            if (result.GroupBy(p => p.GameId, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
            {
                throw new InvalidOperationException("Duplicate game plugin IDs are installed.");
            }

            return result;
        }

        private static IntPtr ResolveNativeLibrary(Assembly _, string libraryName)
        {
            // Native assets are shared by all plugins in runtimes/<rid>/native rather than beside each DLL.
            var directory = Path.Combine(AppContext.BaseDirectory, "runtimes", RuntimeInformation.RuntimeIdentifier, "native");
            var extension =
                OperatingSystem.IsWindows() ? ".dll"
                : OperatingSystem.IsMacOS() ? ".dylib"
                : ".so";
            var names = OperatingSystem.IsWindows()
                ? new[] { libraryName, libraryName + extension }
                : new[] { libraryName, libraryName + extension, "lib" + libraryName + extension };
            foreach (var name in names)
            {
                if (NativeLibrary.TryLoad(Path.Combine(directory, name), out var handle))
                    return handle;
            }
            return IntPtr.Zero;
        }
    }
}
