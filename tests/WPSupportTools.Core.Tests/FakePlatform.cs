using WPSupportTools.Core.Platform;

namespace WPSupportTools.Core.Tests;

/// <summary>Windows "de mentira" para testar as regras sem tocar no computador.</summary>
internal sealed class FakePlatform
{
    public FakeServices Services { get; } = new();
    public FakePrintQueue Queue { get; } = new();
    public List<PrintJobInfo> Jobs => Queue.Jobs;
    public FakeProcesses Processes { get; } = new();
    public DateTime Now { get; set; } = new(2026, 10, 20, 10, 0, 0);

    public PlatformServices Build() => new(Services, Queue, Processes, () => Now);
}

internal sealed class FakePrintQueue : IPrintQueue
{
    public List<PrintJobInfo> Jobs { get; } = [];
    public HashSet<string> InstalledPrinters { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Cancelled { get; } = [];

    /// <summary>Simula documento que continua travado mesmo após cancelar e reiniciar.</summary>
    public bool JobsSurviveCancel { get; set; }

    public IReadOnlyList<PrintJobInfo> ReadJobs() => Jobs.ToList();

    public bool CancelAllJobs(string printerName)
    {
        if (!InstalledPrinters.Contains(printerName))
            return false;

        Cancelled.Add(printerName);
        if (!JobsSurviveCancel)
            Jobs.RemoveAll(j => string.Equals(j.PrinterName, printerName, StringComparison.OrdinalIgnoreCase));
        return true;
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
