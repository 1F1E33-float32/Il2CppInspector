using Il2CppInspector.Cpp.UnityHeaders;
using Il2CppInspector.Model;
using Il2CppInspector.Outputs;
using Il2CppInspector.Plugins;
using Il2CppInspector.Reflection;
using Inspector = Il2CppInspector.Il2CppInspector;

namespace Il2CppInspector.Analysis;

/// <summary>Loads, models and exports IL2CPP data without depending on a host or transport.</summary>
public sealed class AnalysisRunner(Action<OperationProgress> progress, Action<string> log, CancellationToken cancellationToken = default)
{
    private readonly Action<OperationProgress> _progress = progress;
    private readonly Action<string> _log = log;
    private readonly CancellationToken _cancellationToken = cancellationToken;
    private OperationProgress? _active;

    public void Run(AnalysisRequest options)
    {
        using var log = new AnalysisLog(_log, _cancellationToken);
        _cancellationToken.ThrowIfCancellationRequested();

        // Resolve an explicit plugin before parsing its input files.
        if (options.Game is not null)
            GamePlugins.Get(options.Game);

        LoadOptions loadOptions = new()
        {
            StartupMetadataPath = options.StartupMetadataFile,
            Game = options.Game,
            ProgressCallback = Report,
            CancellationToken = _cancellationToken,
        };

        if (!string.IsNullOrEmpty(options.ImageBase))
        {
            try
            {
                loadOptions.ImageBase = Convert.ToUInt64(options.ImageBase, 16);
            }
            catch (Exception ex) when (ex is FormatException or OverflowException)
            {
                throw new ArgumentException($"Invalid image base address: {options.ImageBase}", ex);
            }
        }

        UnityVersion unityVersion = null;
        if (!string.IsNullOrEmpty(options.UnityVersion))
        {
            try
            {
                unityVersion = new UnityVersion(options.UnityVersion);
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException or OverflowException)
            {
                throw new ArgumentException($"Invalid Unity version: {options.UnityVersion}", ex);
            }
            _ = UnityHeaders.GetHeadersForVersion(unityVersion);
        }

        AnalysisLog.WriteLine("Loading IL2CPP data...");

        List<Inspector> il2cppList = null;
        RunStage(
            "Loading IL2CPP data",
            () =>
                il2cppList = Inspector.LoadFromFile(
                    options.BinaryFile,
                    options.MetadataFile,
                    loadOptions,
                    static (_, message) => AnalysisLog.WriteLine(message)
                )
        );
        if (il2cppList.Count == 0)
            throw new InvalidDataException("No IL2CPP images were loaded.");

        var debugFormats = options.Targets.Contains("Debug") ? il2cppList.Select(static i => DebugOutput.SelectFormat(i.BinaryImage)).ToArray() : [];

        AnalysisLog.WriteLine($"Loaded {il2cppList.Count} image(s).");
        AnalysisLog.WriteLine();

        var outputBase = options.OutputDir;
        Directory.CreateDirectory(outputBase);

        for (var imageIndex = 0; imageIndex < il2cppList.Count; imageIndex++)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var il2cpp = il2cppList[imageIndex];
            var output = il2cppList.Count > 1 ? $"{outputBase}-{imageIndex}" : outputBase;

            if (il2cppList.Count > 1)
            {
                AnalysisLog.WriteLine($"=== Processing image {imageIndex} ===");
            }

            TypeModel model = null;
            RunStage("Building type model", progress => model = new TypeModel(il2cpp, progress));

            // DummyDll with per-assembly progress
            var dllOut = Path.Combine(output, "DummyDll");
            AnalysisLog.WriteLine($"Generating DummyDlls -> {dllOut}");
            RunStage("Generating DummyDlls", progress => new AssemblyShims(model) { ProgressCallback = progress }.Write(dllOut));

            var streamExports = il2cpp.Metadata.GamePlugin?.StreamExports == true;

            if (options.Targets.Count > 0 || streamExports)
            {
                AppModel appModel = null;
                var targetUnity = unityVersion ?? il2cpp.Metadata.GamePlugin?.DefaultUnityVersion;
                RunStage(
                    "Building application model",
                    progress => appModel = new AppModel(model, false).Build(targetUnity, progressCallback: progress)
                );

                var scriptTargets = options.Targets.Where(static t => t != "Debug").ToArray();
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
                    AnalysisLog.WriteLine($"Generating {target} script -> {scriptDir}");
                    if (firstScriptDir is null)
                    {
                        WriteScriptInputs(appModel, scriptDir, supplementMetadata);
                        firstScriptDir = scriptDir;
                    }
                    else
                    {
                        RunStage(
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
                    RunStage(
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
                    AnalysisLog.WriteLine($"Generating PDB -> {pdbOut}");
                    RunStage(
                        "Generating PDB",
                        () => pdb = new PdbOutput(appModel).Write(pdbOut, static (_, message) => AnalysisLog.WriteLine(message))
                    );
                    AnalysisLog.WriteLine(
                        $"PDB: {pdb.Functions} functions, {pdb.TypedFunctions} typed, {pdb.Globals} globals, {pdb.TypeRecords} type records."
                    );
                    if (!pdb.HasCodeView)
                        AnalysisLog.WriteLine("PE has no RSDS record; load the generated PDB manually in the debugger.");
                }
                else if (debugFormats.Length > 0 && debugFormats[imageIndex] == DebugSymbolFormat.Dwarf)
                {
                    var dwarfOut = Path.Combine(output, Path.GetFileName(options.BinaryFile));
                    DwarfOutputResult dwarf = null;
                    AnalysisLog.WriteLine($"Embedding DWARF -> {dwarfOut}");
                    RunStage(
                        "Embedding DWARF",
                        () =>
                            dwarf = new DwarfOutput(appModel).WriteImage(
                                dwarfOut,
                                options.BinaryFile,
                                static (_, message) => AnalysisLog.WriteLine(message)
                            )
                    );
                    AnalysisLog.WriteLine(
                        $"DWARF: {dwarf.Functions} functions, {dwarf.TypedFunctions} typed, {dwarf.Globals} globals, {dwarf.TypeRecords} type records, {dwarf.CompilationUnits} units."
                    );
                    AnalysisLog.WriteLine($"Load {dwarfOut}; DWARF is embedded in the binary.");
                }
            }

            AnalysisLog.WriteLine();
        }

        _cancellationToken.ThrowIfCancellationRequested();
        AnalysisLog.WriteLine("Done.");
    }

    // With Debug the debugger symbols carry the types, so the header is skipped and the JSON supplements them.
    private void WriteScriptInputs(AppModel appModel, string directory, bool supplementMetadata)
    {
        if (!supplementMetadata)
            RunStage(
                "Generating C++ types",
                progress =>
                    new CppScaffolding(appModel, useBetterArraySize: true) { ProgressCallback = progress }.WriteTypes(
                        Path.Combine(directory, "il2cpp.h")
                    )
            );
        RunStage(
            "Generating JSON metadata",
            progress =>
                new JSONMetadata(appModel) { SupplementDebugInfo = supplementMetadata, ProgressCallback = progress }.Write(
                    Path.Combine(directory, "il2cpp.json")
                )
        );
    }

    private void Report(OperationProgress progress)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        if (_active is { } previous && previous.Label != progress.Label && previous.Detail is not ("done" or "failed" or "cancelled"))
        {
            var total = Math.Max(0, previous.Total);
            _progress?.Invoke(previous with { Current = total, Total = total, Detail = "done" });
        }
        _active = progress;
        // Readers may report their last work unit before writing or finalizing. Only close a stage once it returns.
        if (progress.Total >= 0 && progress.Current >= progress.Total && progress.Detail is not ("done" or "failed" or "cancelled"))
            return;
        _progress?.Invoke(progress);
    }

    private void RunStage(string label, Action<Action<OperationProgress>> action)
    {
        Report(new OperationProgress(label, 0, -1));
        try
        {
            action(Report);
            var last = _active.Value;
            var total = Math.Max(0, last.Total);
            Report(last with { Current = total, Total = total, Detail = "done" });
        }
        catch (Exception ex)
        {
            var cancelled =
                ex is OperationCanceledException
                || ex is AggregateException aggregate && aggregate.Flatten().InnerExceptions.All(static inner => inner is OperationCanceledException);
            _progress?.Invoke(_active.Value with { Detail = cancelled ? "cancelled" : "failed" });
            if (cancelled && ex is not OperationCanceledException)
                throw new OperationCanceledException("Analysis was cancelled.", ex, _cancellationToken);
            throw;
        }
    }

    private void RunStage(string label, Action action) => RunStage(label, _ => action());
}
