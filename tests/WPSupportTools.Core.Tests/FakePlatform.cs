using WPSupportTools.Core.Platform;

namespace WPSupportTools.Core.Tests;

/// <summary>Windows "de mentira" para testar as regras sem tocar no computador.</summary>
internal sealed class FakePlatform : IDisposable
{
    public FakeServices Services { get; } = new();
    public List<PrintJobInfo> Jobs { get; } = [];
    public FakeProcesses Processes { get; } = new();
    public DateTime Now { get; set; } = new(2026, 10, 20, 10, 0, 0);
    public string SpoolFolder { get; } = Path.Combine(Path.GetTempPath(), "wpst-spool-" + Guid.NewGuid().ToString("N"));
    public string? ConfiguredSpoolFolder { get; set; }

    public FakePlatform()
    {
        Directory.CreateDirectory(SpoolFolder);
        ConfiguredSpoolFolder = SpoolFolder;
    }

    public PlatformServices Build() => new(
        Services, new FakePrintJobs(Jobs), new FakeSpool(this), Processes, () => Now);

    public string AddSpoolFile(string name)
    {
        var path = Path.Combine(SpoolFolder, name);
        File.WriteAllText(path, "x");
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(SpoolFolder))
            Directory.Delete(SpoolFolder, recursive: true);
    }

    private sealed class FakePrintJobs(List<PrintJobInfo> jobs) : IPrintJobReader
    {
        public IReadOnlyList<PrintJobInfo> ReadJobs() => jobs;
    }

    private sealed class FakeSpool(FakePlatform owner) : ISpoolDirectory
    {
        public string StandardPath => owner.SpoolFolder;
        public string? ConfiguredPath() => owner.ConfiguredSpoolFolder;
    }
}

internal sealed class FakeServices : IWindowsServices
{
    public ServiceInfo? Spooler { get; set; } = Running();
    public List<string> Calls { get; } = [];
    public bool TimeOut { get; set; }

    public static ServiceInfo Running() => new("Spooler", "Spooler de Impressão", ServiceState.Running, false, []);

    public ServiceInfo? Find(string serviceName) =>
        string.Equals(serviceName, "Spooler", StringComparison.OrdinalIgnoreCase) ? Spooler : null;

    public void Stop(string serviceName, TimeSpan timeout)
    {
        Calls.Add("stop " + serviceName);
        if (TimeOut)
            throw new TimeoutException();
        Spooler = Spooler! with { State = ServiceState.Stopped };
    }

    public void Start(string serviceName, TimeSpan timeout)
    {
        Calls.Add("start " + serviceName);
        if (TimeOut)
            throw new TimeoutException();
        Spooler = Spooler! with { State = ServiceState.Running };
    }
}

internal sealed class FakeProcesses : IProcessLauncher
{
    public bool TSPrintRunning { get; set; } = true;
    public bool OpensSuccessfully { get; set; } = true;
    public HashSet<string> ExistingFiles { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Started { get; } = [];

    public IReadOnlyList<RunningProcess> FindInCurrentSession(string processName) =>
        processName == "TSPrint" && TSPrintRunning ? [new RunningProcess(1234, "TSPrint", "10.6.0.23")] : [];

    public bool FileExists(string path) => ExistingFiles.Contains(path);

    public void Start(string executablePath)
    {
        Started.Add(executablePath);
        TSPrintRunning = OpensSuccessfully;
    }
}
