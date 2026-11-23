using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using Il2CppInspector.Cpp.UnityHeaders;
using Il2CppInspector.Model;
using Il2CppInspector.Outputs;
using Il2CppInspector.Plugins;
using Il2CppInspector.Reflection;
using Inspector = Il2CppInspector.Il2CppInspector;

namespace Il2CppInspector.CLI
{
    internal class Options
    {
        public string BinaryFile;
        public string ImageBase;
        public string MetadataFile;
        public string StartupMetadataFile;
        public string Game;
        public string OutputDir = "output";
        public List<string> Targets = [];
        public string UnityVersion;
        public bool JsonProgress;
    }

    internal static class NativeDialogs
    {
        private const int OFN_FILEMUSTEXIST = 0x00001000;
        private const int OFN_PATHMUSTEXIST = 0x00000800;
        private const int OFN_NOCHANGEDIR = 0x00000008;

        private const uint FOS_PICKFOLDERS = 0x00000020;
        private const uint FOS_FORCEFILESYSTEM = 0x00000040;

        [DllImport("comdlg32.dll", EntryPoint = "GetOpenFileNameW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetOpenFileNameW(ref OPENFILENAME lpofn);

        [SupportedOSPlatform("windows")]
        public static unsafe string ShowOpenFileDialog(string title, string filter)
        {
            char* buf = stackalloc char[260];
            buf[0] = '\0';

            OPENFILENAME ofn = new()
            {
                lStructSize = Marshal.SizeOf<OPENFILENAME>(),
                lpstrFilter = filter,
                lpstrFile = (nint)buf,
                nMaxFile = 260,
                lpstrTitle = title,
                Flags = OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR,
            };

            return GetOpenFileNameW(ref ofn) ? new string(buf) : null;
        }

        // IFileOpenDialog COM interfaces for modern folder picker
        [DllImport("ole32.dll")]
        private static extern int CoCreateInstance(ref Guid rclsid, nint pUnkOuter, uint dwClsContext, ref Guid riid, out nint ppv);

        [DllImport("ole32.dll")]
        private static extern int CoInitializeEx(nint pvReserved, uint dwCoInit);

        [DllImport("ole32.dll")]
        private static extern void CoUninitialize();

        [DllImport("ole32.dll", EntryPoint = "CoTaskMemFree")]
        private static extern void CoTaskMemFree(nint pv);

        [SupportedOSPlatform("windows")]
        public static string ShowFolderDialog(string title)
        {
            string result = null;
            Thread thread = new(() =>
            {
                // A fresh STA thread cannot already be initialized; on failure the dialog simply returns null.
                _ = CoInitializeEx(
                    0,
                    2 /* COINIT_APARTMENTTHREADED */
                );
                try
                {
                    result = ShowFolderDialogImpl(title);
                }
                finally
                {
                    CoUninitialize();
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            return result;
        }

        [SupportedOSPlatform("windows")]
        private static unsafe string ShowFolderDialogImpl(string title)
        {
            // CLSID_FileOpenDialog = {DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7}
            Guid clsid = new("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7");
            // IID_IFileOpenDialog = {D57C7288-D4AD-4768-BE02-9D969532D960}
            Guid iid = new("D57C7288-D4AD-4768-BE02-9D969532D960");

            int hr = CoCreateInstance(
                ref clsid,
                0,
                1 /* CLSCTX_INPROC_SERVER */
                ,
                ref iid,
                out nint pDialog
            );
            if (hr < 0)
            {
                return null;
            }

            // Get vtable pointer
            nint* vtable = *(nint**)pDialog;

            // IFileDialog::GetOptions (index 10)
            delegate* unmanaged[Stdcall]<nint, out uint, int> getOptions = (delegate* unmanaged[Stdcall]<nint, out uint, int>)vtable[10];
            getOptions(pDialog, out uint options);

            // IFileDialog::SetOptions (index 9)
            delegate* unmanaged[Stdcall]<nint, uint, int> setOptions = (delegate* unmanaged[Stdcall]<nint, uint, int>)vtable[9];
            setOptions(pDialog, options | FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM);

            // IFileDialog::SetTitle (index 17)
            fixed (char* pTitle = title)
            {
                delegate* unmanaged[Stdcall]<nint, char*, int> setTitle = (delegate* unmanaged[Stdcall]<nint, char*, int>)vtable[17];
                setTitle(pDialog, pTitle);
            }

            // IFileDialog::Show (index 3)
            delegate* unmanaged[Stdcall]<nint, nint, int> show = (delegate* unmanaged[Stdcall]<nint, nint, int>)vtable[3];
            hr = show(pDialog, 0);

            string result = null;
            if (hr >= 0)
            {
                // IFileDialog::GetResult (index 20)
                delegate* unmanaged[Stdcall]<nint, out nint, int> getResult = (delegate* unmanaged[Stdcall]<nint, out nint, int>)vtable[20];
                hr = getResult(pDialog, out nint pItem);

                if (hr >= 0)
                {
                    nint* itemVtable = *(nint**)pItem;
                    // IShellItem::GetDisplayName (index 5)
                    delegate* unmanaged[Stdcall]<nint, uint, out nint, int> getDisplayName = (delegate* unmanaged[Stdcall]<nint, uint, out nint, int>)
                        itemVtable[5];
                    hr = getDisplayName(
                        pItem,
                        0x80058000 /* SIGDN_FILESYSPATH */
                        ,
                        out nint pName
                    );

                    if (hr >= 0)
                    {
                        result = new string((char*)pName);
                        CoTaskMemFree(pName);
                    }

                    // IUnknown::Release
                    delegate* unmanaged[Stdcall]<nint, uint> releaseItem = (delegate* unmanaged[Stdcall]<nint, uint>)itemVtable[2];
                    releaseItem(pItem);
                }
            }

            // IUnknown::Release
            delegate* unmanaged[Stdcall]<nint, uint> release = (delegate* unmanaged[Stdcall]<nint, uint>)vtable[2];
            release(pDialog);

            return result;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct OPENFILENAME
        {
            public int lStructSize;
            public nint hwndOwner;
            public nint hInstance;
            public string lpstrFilter;
            public nint lpstrCustomFilter;
            public int nMaxCustFilter;
            public int nFilterIndex;
            public nint lpstrFile;
            public int nMaxFile;
            public nint lpstrFileTitle;
            public int nMaxFileTitle;
            public string lpstrInitialDir;
            public string lpstrTitle;
            public int Flags;
            public short nFileOffset;
            public short nFileExtension;
            public string lpstrDefExt;
            public nint lCustData;
            public nint lpfnHook;
            public string lpTemplateName;
            public nint pvReserved;
            public int dwReserved;
            public int FlagsEx;
        }
    }

    internal static class Program
    {
        private static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.InputEncoding = Encoding.UTF8;

            if (args.Length == 0)
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    RunInteractive();
                }
                else
                {
                    PrintHelp();
                }

                return;
            }

            Options options = ParseArgs(args);
            if (options == null)
            {
                return;
            }

            try
            {
                Run(options);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                Environment.ExitCode = 1;
            }
        }

        private static Options ParseArgs(string[] args)
        {
            Options opts = new();
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
                        if (target == null)
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
                        opts.JsonProgress = true;
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
                        opts.JsonProgress
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
  Il2CppInspector                      (Windows: open file dialogs)

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
      --unity-version <v>   Unity version override (e.g. 2021.3.0f1)
      --image-base <hex>    Image base address for ELF memory dumps (hex)
      --json-progress       Write progress and status as JSON lines (used by the GUI)
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

        [SupportedOSPlatform("windows")]
        private static void RunInteractive()
        {
            Console.WriteLine("Il2CppInspector - No arguments provided, opening file dialogs...");
            Console.WriteLine();

            string binary = NativeDialogs.ShowOpenFileDialog(
                "Select IL2CPP binary",
                "IL2CPP Binary (*.so;*.dll)\0*.so;*.dll\0All files (*.*)\0*.*\0"
            );

            if (string.IsNullOrEmpty(binary))
            {
                Console.Error.WriteLine("No binary file selected.");
                return;
            }

            Console.WriteLine($"Binary: {binary}");

            GamePlugin binaryPlugin;
            try
            {
                using var input = File.OpenRead(binary);
                binaryPlugin = GamePlugins.Select(null, [], input);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                Environment.ExitCode = 1;
                return;
            }
            string metadata = null;
            if (binaryPlugin == null || binaryPlugin.RequiredInputs.HasFlag(GamePluginInputs.Metadata))
            {
                metadata = NativeDialogs.ShowOpenFileDialog("Select global-metadata.dat", "Metadata (*.dat)\0*.dat\0All files (*.*)\0*.*\0");
                if (string.IsNullOrEmpty(metadata))
                {
                    Console.Error.WriteLine("No metadata file selected.");
                    return;
                }
                Console.WriteLine($"Metadata: {metadata}");
            }

            string outputDir = NativeDialogs.ShowFolderDialog("Select output folder");

            if (string.IsNullOrEmpty(outputDir))
            {
                Console.Error.WriteLine("No output folder selected.");
                return;
            }

            Console.WriteLine($"Output: {outputDir}");
            Console.WriteLine();

            Run(
                new Options
                {
                    BinaryFile = binary,
                    MetadataFile = metadata,
                    OutputDir = outputDir,
                }
            );
        }

        private static void Run(Options options)
        {
            ProgressBar.JsonLines = options.JsonProgress;

            // Resolve an explicit plugin before parsing its input files.
            if (options.Game != null)
                GamePlugins.Get(options.Game);

            LoadOptions loadOptions = new()
            {
                StartupMetadataPath = options.StartupMetadataFile,
                Game = options.Game,
                ProgressCallback = ProgressBar.Update,
            };

            if (!string.IsNullOrEmpty(options.ImageBase))
            {
                try
                {
                    loadOptions.ImageBase = Convert.ToUInt64(options.ImageBase, 16);
                }
                catch
                {
                    Console.Error.WriteLine($"Invalid image base address: {options.ImageBase}");
                    Environment.ExitCode = 1;
                    return;
                }
            }

            UnityVersion unityVersion = null;
            if (!string.IsNullOrEmpty(options.UnityVersion))
            {
                try
                {
                    unityVersion = new UnityVersion(options.UnityVersion);
                }
                catch
                {
                    Console.Error.WriteLine($"Invalid Unity version: {options.UnityVersion}");
                    Environment.ExitCode = 1;
                    return;
                }
            }

            Console.WriteLine("Loading IL2CPP data...");

            List<Inspector> il2cppList;
            try
            {
                il2cppList = Inspector.LoadFromFile(options.BinaryFile, options.MetadataFile, loadOptions, (_, msg) => ProgressBar.WriteStatus(msg));
            }
            catch (Exception ex)
            {
                ProgressBar.Fail();
                Console.Error.WriteLine(ex.Message);
                Environment.ExitCode = 1;
                return;
            }

            if (il2cppList == null || il2cppList.Count == 0)
            {
                Environment.ExitCode = 1;
                return;
            }

            var debugFormats = options.Targets.Contains("Debug") ? il2cppList.Select(i => DebugOutput.SelectFormat(i.BinaryImage)).ToArray() : [];

            Console.WriteLine($"Loaded {il2cppList.Count} image(s).");
            Console.WriteLine();

            string outputBase = options.OutputDir;
            Directory.CreateDirectory(outputBase);

            for (int imageIndex = 0; imageIndex < il2cppList.Count; imageIndex++)
            {
                Inspector il2cpp = il2cppList[imageIndex];
                string output = il2cppList.Count > 1 ? $"{outputBase}-{imageIndex}" : outputBase;

                if (il2cppList.Count > 1)
                {
                    Console.WriteLine($"=== Processing image {imageIndex} ===");
                }

                TypeModel model = null;
                ProgressBar.Run("Building type model", progress => model = new TypeModel(il2cpp, progress));

                // DummyDll with per-assembly progress
                string dllOut = Path.Combine(output, "DummyDll");
                Console.WriteLine($"Generating DummyDlls -> {dllOut}");
                ProgressBar.Run("Generating DummyDlls", progress => new AssemblyShims(model) { ProgressCallback = progress }.Write(dllOut));

                var streamExports = il2cpp.Metadata.GamePlugin?.StreamExports == true;

                if (options.Targets.Count > 0 || streamExports)
                {
                    AppModel appModel = null;
                    var targetUnity = unityVersion ?? il2cpp.Metadata.GamePlugin?.DefaultUnityVersion;
                    ProgressBar.Run(
                        "Building application model",
                        progress => appModel = new AppModel(model, false).Build(targetUnity, progressCallback: progress)
                    );

                    var scriptTargets = options.Targets.Where(t => t != "Debug").ToArray();
                    var supplementMetadata = options.Targets.Contains("Debug");
                    if (options.Targets.Count == 0)
                    {
                        // Streaming plugins still export the header and JSON metadata without a script target.
                        WriteScriptInputs(appModel, output, supplementMetadata: false);
                    }

                    // Each Script_<target> folder holds its script with its own header and JSON so it can be moved on its own.
                    // Generating them is the expensive part, so later folders copy the first folder's files.
                    string firstScriptDir = null;
                    foreach (var target in scriptTargets)
                    {
                        var scriptDir = Path.Combine(output, "Script_" + target);
                        Directory.CreateDirectory(scriptDir);
                        Console.WriteLine($"Generating {target} script -> {scriptDir}");
                        if (firstScriptDir == null)
                        {
                            WriteScriptInputs(appModel, scriptDir, supplementMetadata);
                            firstScriptDir = scriptDir;
                        }
                        else
                        {
                            ProgressBar.Run(
                                "Copying C++ types and JSON metadata",
                                () =>
                                {
                                    if (!supplementMetadata)
                                        File.Copy(Path.Combine(firstScriptDir, "il2cpp.h"), Path.Combine(scriptDir, "il2cpp.h"), true);
                                    File.Copy(Path.Combine(firstScriptDir, "il2cpp.json"), Path.Combine(scriptDir, "il2cpp.json"), true);
                                }
                            );
                        }

                        var pyOut = Path.Combine(scriptDir, "il2cpp.py");
                        ProgressBar.Run(
                            $"Generating {target} Python script",
                            () =>
                                new PythonScript(appModel).WriteScriptToFile(
                                    pyOut,
                                    target,
                                    Path.Combine(scriptDir, "il2cpp.h"),
                                    Path.Combine(scriptDir, "il2cpp.json"),
                                    includeTypeHeader: !supplementMetadata
                                )
                        );
                    }

                    // Debug outputs materialize native layouts; finish streaming script exports first.
                    if (debugFormats.Length > 0 && debugFormats[imageIndex] == DebugSymbolFormat.Pdb)
                    {
                        var pdbOut = Path.Combine(output, Path.GetFileNameWithoutExtension(options.BinaryFile) + ".pdb");
                        PdbOutputResult pdb = null;
                        Console.WriteLine($"Generating PDB -> {pdbOut}");
                        ProgressBar.Run("Generating PDB", () => pdb = new PdbOutput(appModel).Write(pdbOut));
                        Console.WriteLine(
                            $"PDB: {pdb.Functions} functions, {pdb.TypedFunctions} typed, {pdb.Globals} globals, {pdb.TypeRecords} type records."
                        );
                        if (!pdb.HasCodeView)
                            Console.WriteLine("PE has no RSDS record; load the generated PDB manually in the debugger.");
                    }
                    else if (debugFormats.Length > 0 && debugFormats[imageIndex] == DebugSymbolFormat.Dwarf)
                    {
                        var dwarfOut = Path.Combine(output, Path.GetFileName(options.BinaryFile));
                        DwarfOutputResult dwarf = null;
                        Console.WriteLine($"Embedding DWARF -> {dwarfOut}");
                        ProgressBar.Run("Embedding DWARF", () => dwarf = new DwarfOutput(appModel).WriteImage(dwarfOut, options.BinaryFile));
                        Console.WriteLine(
                            $"DWARF: {dwarf.Functions} functions, {dwarf.TypedFunctions} typed, {dwarf.Globals} globals, {dwarf.TypeRecords} type records, {dwarf.CompilationUnits} units."
                        );
                        Console.WriteLine($"Load {dwarfOut}; DWARF is embedded in the binary.");
                    }
                }

                Console.WriteLine();
            }

            Console.WriteLine("Done.");
        }

        // With Debug the debugger symbols carry the types, so the header is skipped and the JSON supplements them.
        private static void WriteScriptInputs(AppModel appModel, string directory, bool supplementMetadata)
        {
            if (!supplementMetadata)
                ProgressBar.Run(
                    "Generating C++ types",
                    progress =>
                        new CppScaffolding(appModel, useBetterArraySize: true) { ProgressCallback = progress }.WriteTypes(
                            Path.Combine(directory, "il2cpp.h")
                        )
                );
            ProgressBar.Run(
                "Generating JSON metadata",
                progress =>
                    new JSONMetadata(appModel) { SupplementDebugInfo = supplementMetadata, ProgressCallback = progress }.Write(
                        Path.Combine(directory, "il2cpp.json")
                    )
            );
        }
    }
}
