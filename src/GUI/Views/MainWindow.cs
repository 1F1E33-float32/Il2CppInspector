using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Il2CppInspector.Analysis;
using Il2CppInspector.GUI.Controls;
using Il2CppInspector.GUI.Models;
using Il2CppInspector.GUI.Theme;
using Il2CppInspector.Plugins;

namespace Il2CppInspector.GUI.Views;

internal sealed partial class MainWindow : Window
{
    private static readonly string[] Targets = ["IDA", "Ghidra", "BinaryNinja", "Debug"];
    private readonly bool[] _targets = new bool[4];
    private readonly GamePicker _picker = new();
    private readonly TextBox _binary = Ui.Input("GameAssembly.dll or libil2cpp.so");
    private readonly TextBox _metadata = Ui.Input("global-metadata.dat");
    private readonly TextBox _output = Ui.Input("Output directory");
    private readonly TextBox _startup = Ui.Input("Detected beside the metadata file");
    private readonly TextBox _unityVersion = Ui.Input("Detected, e.g. 2021.3.0f1");
    private readonly TextBox _imageBase = Ui.Input("Hex, for ELF memory dumps");
    private readonly Dictionary<TextBox, Button> _pickButtons = [];
    private readonly List<(AnalysisJob Job, TaskRow Row)> _jobs = [];
    private readonly StackPanel _taskList = new() { Margin = new Thickness(6, 10, 6, 24) };
    private readonly TextBlock _noTasks = Ui.Message("No tasks");
    private readonly TextBlock _error = new()
    {
        Foreground = Palette.DangerText,
        TextWrapping = TextWrapping.Wrap,
        IsVisible = false,
    };
    private readonly Grid _root = new();
    private readonly Border _details = new();
    private readonly Canvas _settingsLayer = new();
    private readonly DispatcherTimer _pump = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly StackPanel _launcherContent = new() { Spacing = 14, Margin = new Thickness(16, 16, 16, 24) };
    private Button _start;
    private readonly Border _settings;
    private AnalysisJob _selected;
    private bool _picking;
    private bool _pluginsLoaded;
    private bool _closing;
    private bool _canClose;

    internal MainWindow()
    {
        Title = "Il2CppInspector";
        Width = 1440;
        Height = 880;
        MinWidth = 1200;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Palette.Background;
        Grid columns = new() { ColumnDefinitions = new ColumnDefinitions("640,280,*") };
        columns.Children.Add(BuildLauncher());
        _taskList.Children.Add(_noTasks);
        Border tasks = new()
        {
            BorderBrush = Palette.Separator,
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = new ScrollArea(_taskList),
        };
        Grid.SetColumn(tasks, 1);
        columns.Children.Add(tasks);
        Grid.SetColumn(_details, 2);
        columns.Children.Add(_details);
        _root.Children.Add(columns);
        _settings = BuildSettings();
        _settings.IsVisible = false;
        _settingsLayer.Children.Add(_settings);
        _root.Children.Add(_settingsLayer);
        Content = _root;
        _picker.SelectionChanged += () =>
        {
            UpdateInputs();
            UpdateLauncherHeight();
        };
        _root.SizeChanged += (_, _) =>
        {
            UpdateLauncherHeight();
            PositionSettings();
        };
        AddHandler(
            KeyDownEvent,
            (_, e) =>
            {
                if (e.Key == Key.Escape && _settings.IsVisible)
                {
                    ToggleSettings(false);
                    e.Handled = true;
                }
            },
            RoutingStrategies.Tunnel
        );
        _pump.Tick += (_, _) => Pump();
        Opened += async (_, _) =>
        {
            _pump.Start();
            try
            {
                var plugins = await Task.Run(static () =>
                    GamePlugins.Installed.OrderBy(static p => p.GameId, StringComparer.OrdinalIgnoreCase).ToArray()
                );
                _pluginsLoaded = true;
                _picker.Load(plugins);
            }
            catch (Exception ex)
            {
                _picker.ShowError(ex.Message);
            }
            UpdateInputs();
        };
        Closing += OnClosing;
        Closed += (_, _) => _pump.Stop();
        RebuildDetails();
        UpdateInputs();
    }

    private Control BuildLauncher()
    {
        _launcherContent.Children.Add(_picker);
        _launcherContent.Children.Add(PathField("Binary", _binary));
        _launcherContent.Children.Add(PathField("Metadata", _metadata));
        _launcherContent.Children.Add(PathField("Output", _output));
        StackPanel segments = new() { Orientation = Orientation.Horizontal, Spacing = 2 };
        for (int index = 0; index < Targets.Length; index++)
        {
            int target = index;
            Button segment = new() { Content = Targets[index] };
            segment.Classes.Add("segment");
            segment.Click += (_, _) =>
            {
                _targets[target] = !_targets[target];
                segment.Classes.Set("active", _targets[target]);
            };
            segments.Children.Add(segment);
        }
        Border track = new()
        {
            Height = 28,
            Padding = new Thickness(2),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = Brush.Parse("#17FFFFFF"),
            Background = Brush.Parse("#47000000"),
            Child = segments,
        };
        _start = Ui.Button("Start", Start, "primary");
        _launcherContent.Children.Add(
            new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children = { track, Ui.Button("Settings", () => ToggleSettings(!_settings.IsVisible)), _start },
            }
        );
        _launcherContent.Children.Add(_error);
        return new Border
        {
            Background = Brush.Parse("#05FFFFFF"),
            BorderBrush = Palette.Separator,
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = new ScrollArea(_launcherContent),
        };
    }

    private Control PathField(string label, TextBox input)
    {
        Grid row = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        row.Children.Add(input);
        Button select = Ui.Button("Select", () => _ = Pick(input));
        select.Margin = new Thickness(6, 0, 0, 0);
        Grid.SetColumn(select, 1);
        row.Children.Add(select);
        _pickButtons.Add(input, select);
        return Ui.Labeled(label, row);
    }

    private void UpdateLauncherHeight()
    {
        // The table shrinks first; paths and actions retain their exact height. The launcher scrolls below the table minimum.
        _error.Measure(new Size(607, double.PositiveInfinity));
        double fixedHeight = 16 + 24 + 3 * 51 + 28 + 4 * 14 + (_error.IsVisible ? _error.DesiredSize.Height + 14 : 0);
        _picker.Height = Math.Max(150, Math.Min(_picker.PreferredHeight, _root.Bounds.Height - fixedHeight));
    }

    private void UpdateInputs()
    {
        var inputs = _picker.Inputs;
        _binary.IsEnabled = inputs is null || inputs.Value.HasFlag(GamePluginInputs.Binary);
        _metadata.IsEnabled = inputs is null || inputs.Value.HasFlag(GamePluginInputs.Metadata);
        _startup.IsEnabled = inputs is null || inputs.Value.HasFlag(GamePluginInputs.StartupMetadata);
        foreach (var (input, button) in _pickButtons)
            button.IsEnabled = input.IsEnabled && !_picking && !_closing;
        _start.IsEnabled = _pluginsLoaded && !_closing;
    }

    private async Task Pick(TextBox input)
    {
        if (_picking || !input.IsEnabled)
            return;
        _picking = true;
        UpdateInputs();
        try
        {
            string path;
            if (input == _output)
            {
                var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Select output directory" });
                path = folders.FirstOrDefault()?.TryGetLocalPath();
                foreach (var folder in folders)
                    folder.Dispose();
            }
            else
            {
                var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Select input file" });
                path = files.FirstOrDefault()?.TryGetLocalPath();
                foreach (var file in files)
                    file.Dispose();
            }
            if (path is not null && input.IsEnabled)
            {
                input.Text = path;
                input.CaretIndex = path.Length;
                if (input == _binary && _metadata.IsEnabled && string.IsNullOrEmpty(_metadata.Text))
                {
                    var parent = Path.GetDirectoryName(path);
                    _metadata.Text = Directory
                        .EnumerateDirectories(parent)
                        .Select(static directory => Path.Combine(directory, "il2cpp_data", "Metadata", "global-metadata.dat"))
                        .FirstOrDefault(File.Exists);
                }
            }
        }
        catch (Exception ex)
        {
            ShowError($"Cannot open the file dialog: {ex.Message}");
        }
        finally
        {
            _picking = false;
            UpdateInputs();
        }
    }

    private void Start()
    {
        ShowError(null);
        try
        {
            string game = _picker.GetGame();
            GamePluginInputs? inputs = _picker.Inputs;
            AnalysisRequest request = new()
            {
                Game = game,
                BinaryFile = InputPath(_binary, "Binary", inputs is null || inputs.Value.HasFlag(GamePluginInputs.Binary)),
                MetadataFile = InputPath(_metadata, "Metadata", inputs?.HasFlag(GamePluginInputs.Metadata) == true),
                StartupMetadataFile = InputPath(_startup, "Startup metadata", false),
                OutputDir = NormalizedPath(_output) ?? throw new ArgumentException("An output directory is required."),
                UnityVersion = OptionalText(_unityVersion),
                ImageBase = OptionalText(_imageBase),
                Targets = Targets.Where((_, index) => _targets[index]).ToList(),
            };
            foreach ((AnalysisJob job, _) in _jobs)
            {
                if (job.Status == JobStatus.Running && AnalysisJob.OutputsOverlap(request.OutputDir, job.Request.OutputDir))
                    throw new InvalidOperationException(
                        $"Output directory {request.OutputDir} overlaps running task {job.Title} ({job.Request.OutputDir})."
                    );
            }
            AnalysisJob added = new(request);
            TaskRow row = new(added, () => Select(added));
            _jobs.Add((added, row));
            _taskList.Children.Add(row);
            _noTasks.IsVisible = false;
            Select(added);
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private void ShowError(string message)
    {
        _error.Text = message;
        _error.IsVisible = message is not null;
        UpdateLauncherHeight();
    }

    private void Select(AnalysisJob job)
    {
        _selected = job;
        foreach ((AnalysisJob item, TaskRow row) in _jobs)
            row.Update(item == _selected);
        RebuildDetails();
    }

    private void Pump()
    {
        foreach ((AnalysisJob job, TaskRow row) in _jobs)
        {
            bool wasRunning = job.Status == JobStatus.Running;
            bool changed = job.Drain();
            if (changed || wasRunning)
            {
                row.Update(job == _selected);
                if (job == _selected)
                    RefreshDetails(changed);
            }
        }
    }

    private async void OnClosing(object sender, WindowClosingEventArgs e)
    {
        if (_canClose || _jobs.All(static pair => pair.Job.Completion.IsCompleted))
            return;
        e.Cancel = true;
        if (_closing)
            return;
        _closing = true;
        UpdateInputs();
        foreach (var (job, _) in _jobs)
            job.Cancel();
        await Task.WhenAll(_jobs.Select(static pair => pair.Job.Completion));
        Pump();
        _canClose = true;
        Close();
    }

    private static string OptionalText(TextBox input) => string.IsNullOrWhiteSpace(input.Text) ? null : input.Text.Trim();

    private static string NormalizedPath(TextBox input)
    {
        var text = input.Text?.Trim().Trim('"');
        return string.IsNullOrEmpty(text) ? null : Path.GetFullPath(text);
    }

    private static string InputPath(TextBox input, string label, bool required)
    {
        if (!input.IsEnabled)
            return null;
        var path = NormalizedPath(input);
        if (path is null && required)
            throw new ArgumentException($"{label} file is required.");
        if (path is not null && !File.Exists(path))
            throw new FileNotFoundException($"{label} file not found: {path}", path);
        return path;
    }
}
