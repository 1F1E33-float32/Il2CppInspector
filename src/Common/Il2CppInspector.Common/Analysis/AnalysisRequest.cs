namespace Il2CppInspector.Analysis;

/// <summary>Input files and output targets shared by the desktop and command-line hosts.</summary>
public sealed class AnalysisRequest
{
    public string BinaryFile { get; set; }
    public string MetadataFile { get; set; }
    public string StartupMetadataFile { get; set; }
    public string OutputDir { get; set; } = "output";
    public string Game { get; set; }
    public List<string> Targets { get; set; } = [];
    public string UnityVersion { get; set; }
    public string ImageBase { get; set; }
}
