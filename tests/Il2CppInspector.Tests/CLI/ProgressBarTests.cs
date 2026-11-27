using Il2CppInspector.CLI;

namespace Il2CppInspector.Tests.CLI;

internal static class ProgressBarTests
{
    internal static void Run(string[] args)
    {
        TextWriter original = Console.Out;
        using var output = new StringWriter();
        string success;
        string failure;
        try
        {
            Console.SetOut(output);
            for (var i = 0; i < 100; i++)
                ProgressBar.Update(new OperationProgress("Generating DummyDlls", i, 100));
            ProgressBar.Update(new OperationProgress("Generating DummyDlls", 100, 100, "done"));
            success = output.ToString();
            output.GetStringBuilder().Clear();
            ProgressBar.Update(new OperationProgress("Building type model", 0, -1));
            ProgressBar.Update(new OperationProgress("Building type model", 0, -1, "failed"));
            failure = output.ToString();
            output.GetStringBuilder().Clear();
            ProgressBar.Update(new OperationProgress("Processing relocations", 1000, 2000));
            ProgressBar.Update(new OperationProgress("Processing relocations", 2000, 2000));
            ProgressBar.WriteStatus("Processed 2000 relocations");
        }
        finally
        {
            Console.SetOut(original);
        }
        Check(success.Contains("100%") && success.Contains("done"), "Successful operations finish at 100 percent");
        Check(
            failure.Contains("failed") && !failure.Contains("100%") && !failure.Contains("done"),
            "Failed operations report failure without claiming completion"
        );
        Check(
            output.ToString().Contains("100%") && output.ToString().Contains("Processed 2000 relocations"),
            "Relocation progress finishes before the next status message"
        );
        if (Console.IsOutputRedirected)
        {
            Check(
                !success.Replace(Environment.NewLine, "\n").Contains('\r') && success.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length <= 13,
                "Redirected logs use bounded milestones without carriage-return refreshes"
            );
        }

        output.GetStringBuilder().Clear();
        ProgressBar.JsonLines = true;
        try
        {
            Console.SetOut(output);
            for (var i = 0; i < 100; i++)
                ProgressBar.Update(new OperationProgress("Generating DummyDlls", i, 100, "fixture"));
            ProgressBar.Update(new OperationProgress("Generating DummyDlls", 100, 100, "done"));
            ProgressBar.WriteStatus("Processed 2000 relocations");
        }
        finally
        {
            Console.SetOut(original);
            ProgressBar.JsonLines = false;
        }
        var events = output
            .ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(static line => JsonDocument.Parse(line).RootElement)
            .ToArray();
        Check(
            events.Length < 10
                && events[^2].GetProperty("event").GetString() == "progress"
                && events[^2].GetProperty("current").GetInt64() == 100
                && events[^2].GetProperty("total").GetInt64() == 100
                && events[^2].GetProperty("detail").GetString() == "done"
                && events[^1].GetProperty("event").GetString() == "status"
                && events[^1].GetProperty("message").GetString() == "Processed 2000 relocations",
            "JSON progress writes throttled objects, one per line, and completes before the next status"
        );

        // Phase weights and step counts are estimates; a phase must still end exactly at its planned share.
        var phased = new List<OperationProgress>();
        var plan = new PhasedProgress("Fixture", phased.Add);
        plan.Plan("Short", 1);
        plan.Plan("Long", 3);
        plan.Begin("Short", 2);
        for (int i = 0; i < 5; i++)
            plan.Step();
        var shortEnd = phased[^1].Current;
        plan.Begin("Unplanned", 10);
        plan.Step();
        var unplanned = phased[^1].Current;
        plan.Begin("Long", 1000);
        for (int i = 0; i < 10; i++)
            plan.Step();
        plan.End();
        Check(
            shortEnd == 250 && unplanned == 250 && phased[^1] == new OperationProgress("Fixture", 1000, 1000, "Long"),
            "Phased progress clamps extra steps to the phase share, gives unplanned phases no share and ends at the total"
        );
        Verify(phased, "Phased progress");

        if (args.Length == 1)
            return;
        var relocations = new List<OperationProgress>();
        var package = Inspector.LoadFromFile(args[1], args[2], new LoadOptions { ProgressCallback = relocations.Add }).Single();
        Verify(relocations, "Relocations");
        var types = new List<OperationProgress>();
        var model = new TypeModel(package, types.Add);
        Verify(types, "Type model");
        var assemblies = new List<OperationProgress>();
        new AssemblyShims(model) { ProgressCallback = assemblies.Add }.Write(args[3]);
        Verify(assemblies, "DummyDll");
        var application = new List<OperationProgress>();
        var app = new AppModel(model, false).Build(package.Metadata.GamePlugin?.DefaultUnityVersion, progressCallback: application.Add);
        Verify(application, "Application model");
        var header = new List<OperationProgress>();
        new CppScaffolding(app, useBetterArraySize: true) { ProgressCallback = header.Add }.WriteTypes(Path.Combine(args[3], "il2cpp.h"));
        Verify(header, "C++ types");
        var json = new List<OperationProgress>();
        new JSONMetadata(app) { ProgressCallback = json.Add }.Write(Path.Combine(args[3], "il2cpp.json"));
        Verify(json, "JSON metadata");
    }

    private static void Verify(List<OperationProgress> reports, string label)
    {
        if (label == "Relocations" && reports.Count == 0)
            return;
        Check(reports.Count > 0 && reports[0].Current == 0, $"{label} starts at zero");
        Check(reports.All(p => p.Current >= 0 && p.Current <= p.Total), $"{label} stays within its total");
        Check(reports.Zip(reports.Skip(1)).All(pair => pair.First.Current <= pair.Second.Current), $"{label} updates monotonically");
        Check(reports[^1].Current == reports[^1].Total, $"{label} reports all completed work");
    }
}
