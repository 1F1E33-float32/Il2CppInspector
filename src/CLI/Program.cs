using System.Text;
using System.Text.Json;
using Il2CppInspector.Analysis;
using Il2CppInspector.Outputs;
using Il2CppInspector.Plugins;

namespace Il2CppInspector.CLI;

internal static class Program
{
    private static void Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;
        if (args.Length == 0)
        {
            PrintHelp();
            return;
        }
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancellation.Cancel();
        };
        try
        {
            var options = ParseArgs(args);
            if (options is not null)
            {
                using var refresh = ProgressBar.RefreshContinuously();
                new AnalysisRunner(ProgressBar.Update, ProgressBar.WriteStatus, cancellation.Token).Run(options);
            }
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Cancelled.");
            Environment.ExitCode = 130;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            Environment.ExitCode = 1;
        }
    }

    private static AnalysisRequest ParseArgs(string[] args)
    {
        AnalysisRequest opts = new();
        bool listGames = false;

        for (int i = 0; i < args.Length; i++)
        {
            string value = null;

            bool NeedValue()
            {
                if (i + 1 >= args.Length || args[i + 1].StartsWith('-'))
                {
                    Console.Error.WriteLine($"Option {args[i]} requires a value.");
                    Environment.ExitCode = 1;
                    return false;
                }

                value = args[++i];
                return true;
            }

            switch (args[i])
            {
                case "-i" or "--bin":
                    if (!NeedValue())
                    {
                        return null;
                    }

                    opts.BinaryFile = value;
                    break;
                case "-m" or "--metadata":
                    if (!NeedValue())
                    {
                        return null;
                    }

                    opts.MetadataFile = value;
                    break;
                case "--startup-metadata":
                    if (!NeedValue())
                    {
                        return null;
                    }

                    opts.StartupMetadataFile = value;
                    break;
                case "--game":
                    if (!NeedValue())
                        return null;
                    opts.Game = value;
                    break;
                case "-o" or "--output":
                    if (!NeedValue())
                    {
                        return null;
                    }

                    opts.OutputDir = value;
                    break;
                case "-t" or "--target" or "--script-target":
                    if (!NeedValue())
                    {
                        return null;
                    }

                    var availableTargets = PythonScript.GetAvailableTargets().Append("Debug").ToArray();
                    var target = availableTargets.FirstOrDefault(t => t.Equals(value, StringComparison.OrdinalIgnoreCase));
                    if (target is null)
                    {
                        Console.Error.WriteLine($"Unknown output target: {value}");
                        Console.Error.WriteLine($"Available targets: {string.Join(", ", availableTargets)}");
                        Environment.ExitCode = 1;
                        return null;
                    }
                    if (!opts.Targets.Contains(target))
                        opts.Targets.Add(target);
                    break;
                case "--unity-version":
                    if (!NeedValue())
                    {
                        return null;
                    }

                    opts.UnityVersion = value;
                    break;
                case "--image-base":
                    if (!NeedValue())
                    {
                        return null;
                    }

                    opts.ImageBase = value;
                    break;
                case "--json-progress":
                    ProgressBar.JsonLines = true;
                    break;
                case "--list-games":
                    listGames = true;
                    break;
                case "-h" or "--help":
                    PrintHelp();
                    return null;
                default:
                    Console.Error.WriteLine($"Unknown option: {args[i]}");
                    Environment.ExitCode = 1;
                    PrintHelp();
                    return null;
            }
        }

        if (listGames)
        {
            foreach (var plugin in GamePlugins.Installed.OrderBy(static p => p.GameId, StringComparer.OrdinalIgnoreCase))
            {
                Console.WriteLine(
                    ProgressBar.JsonLines
                        ? JsonSerializer.Serialize(
                            new
                            {
                                gameId = plugin.GameId,
                                inputs = new
                                {
                                    binary = plugin.RequiredInputs.HasFlag(GamePluginInputs.Binary),
                                    metadata = plugin.RequiredInputs.HasFlag(GamePluginInputs.Metadata),
                                    startupMetadata = plugin.RequiredInputs.HasFlag(GamePluginInputs.StartupMetadata),
                                },
                            }
                        )
                        : plugin.GameId
                );
            }
            return null;
        }

        return opts;
    }

    private static void PrintHelp()
    {
        Console.WriteLine(
            @"Il2CppInspector - IL2CPP binary analysis tool

Usage:
  Il2CppInspector [options]

Options:
  -i, --bin <file>          IL2CPP binary file (required by the selected plugin)
  -m, --metadata <file>     global-metadata.dat file (ignored by embedded-metadata plugins)
  -o, --output <dir>        Output directory (default: output)
  -t, --target <t>          Output target: IDA, BinaryNinja, Ghidra, Debug
                            Repeat to select multiple targets: -t IDA -t Debug
                            IDA also writes an _t.py script for idat (tqdm, q, resume)
                            --script-target remains an alias
                            Debug: PE -> PDB (x64); ELF/Mach-O -> DWARF
                            DWARF supports little-endian x86/x64/ARM/ARM64 images
      --startup-metadata <f> Plugin startup metadata (auto-detected beside metadata)
                            Unused input files are ignored according to the plugin
      --game <id>           Game plugin: NAME_REGION_PLATFORM_VERSION
                            e.g. Endfield_CN_Android_x.x.x, NSH_OS_Windows_20260731072817UTC
                            LimbusCompany_OS_Windows_1.119.1 accepts encrypted metadata
                            ABC_CN_Windows_0.75.2 accepts MHY v1 metadata
                            Auto-detect when omitted
      --unity-version <v>   Final/patch Unity version override (e.g. 2021.3.0f1)
      --image-base <hex>    Image base address for ELF memory dumps (hex)
      --json-progress       Write progress and status as JSON lines
      --list-games          List installed game plugin IDs and exit
                            With --json-progress, include input requirements as JSON lines
  -h, --help                Show this help

Output structure:
  <output>/DummyDll/                .NET assembly shim DLLs
  <output>/Script_<t>/il2cpp.py     Python script for IDA, Ghidra or BinaryNinja
  <output>/Script_<t>/il2cpp.h      C++ type header (omitted with Debug)
  <output>/Script_<t>/il2cpp.json   JSON metadata
  <output>/Script_IDA/il2cpp_t.py   IDA terminal script with checkpoint/resume
  <output>/<binary>.pdb             Native PDB symbols and types (Debug on PE)
  <output>/<input-name>             ELF/Mach-O binary with embedded DWARF symbols and types

Disassembler targets with Debug omit type headers; debug symbol arrays in JSON are empty."
        );
    }
}
