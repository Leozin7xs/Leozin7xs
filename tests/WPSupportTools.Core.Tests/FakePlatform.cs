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
    public List<InstalledPrinter> Printers { get; } = [];
    public FakePreferences Preferences { get; } = new();
    public FakeRawPrinter Raw { get; } = new();

    public FakePlatform()
    {
        Modules.Printing.PrintingConstants.SpoolerSettleDelay = TimeSpan.Zero;
        Services.OnStart = Queue.ReloadAfterRestart;
    }

    public PlatformServices Build() =>
        new(Services, Queue, Processes, () => Now, new FakeCatalog(Printers), Preferences, Raw);

    private sealed class FakeCatalog(List<InstalledPrinter> printers) : IPrinterCatalog
    {
        public IReadOnlyList<InstalledPrinter> List() => printers;
    }
}

internal sealed class FakePreferences : IPrinterPreferences
{
    public Dictionary<string, byte[]> DevModes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Writes { get; } = [];
    public bool DriverIgnoresSize { get; set; }

    public byte[] Read(string printerName) => DevModes[printerName];

    public void Write(string printerName, byte[] devMode)
    {
        Writes.Add(printerName);
        if (!DriverIgnoresSize)
            DevModes[printerName] = devMode;
    }

    /// <summary>DEVMODE mínimo: cabeçalho público de 220 bytes + 16 bytes "privados" do driver.</summary>
    public static byte[] NewDevMode(string device, short width, short length, short orientation = 1)
    {
        var data = new byte[220 + 16];
        System.Text.Encoding.Unicode.GetBytes(device).CopyTo(data, 0);
        BitConverter.GetBytes((ushort)220).CopyTo(data, 68);
        BitConverter.GetBytes((ushort)16).CopyTo(data, 70);
        BitConverter.GetBytes(orientation).CopyTo(data, 76);
        BitConverter.GetBytes((short)256).CopyTo(data, 78);
        BitConverter.GetBytes(length).CopyTo(data, 80);
        BitConverter.GetBytes(width).CopyTo(data, 82);
        for (var i = 220; i < data.Length; i++)
            data[i] = (byte)(0xA0 + i - 220);   // parte privada: deve ser preservada
        return data;
    }
}

internal sealed class FakeRawPrinter : IRawPrinter
{
    public List<(string Printer, string Document, string Text)> Sent { get; } = [];

    public void Send(string printerName, string documentName, byte[] data) =>
        Sent.Add((printerName, documentName, System.Text.Encoding.ASCII.GetString(data)));
}

internal sealed class FakePrintQueue : IPrintQueue
{
    public List<PrintJobInfo> Jobs { get; } = [];
    public HashSet<string> InstalledPrinters { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Cancelled { get; } = [];

    /// <summary>Simula documento que continua travado mesmo após cancelar e reiniciar.</summary>
    public bool JobsSurviveCancel { get; set; }

    /// <summary>Simula documento preso em "excluindo" que volta quando o spooler reinicia.</summary>
    public bool CancelledJobsReturnOnRestart { get; set; }

    private readonly List<PrintJobInfo> _pendingDeletion = [];

    public void ReloadAfterRestart()
    {
        Jobs.AddRange(_pendingDeletion);
        _pendingDeletion.Clear();
    }

    public IReadOnlyList<PrintJobInfo> ReadJobs() => Jobs.ToList();

    public bool CancelAllJobs(string printerName)
    {
        if (!InstalledPrinters.Contains(printerName))
            return false;

        Cancelled.Add(printerName);
        if (JobsSurviveCancel)
            return true;

        var removed = Jobs.Where(j => string.Equals(j.PrinterName, printerName, StringComparison.OrdinalIgnoreCase)).ToList();
        Jobs.RemoveAll(removed.Contains);
        if (CancelledJobsReturnOnRestart && Cancelled.Count == 1)
            _pendingDeletion.AddRange(removed);
        return true;
    }
}

internal sealed class FakeServices : IWindowsServices
{
    public ServiceInfo? Spooler { get; set; } = Running();
    public List<string> Calls { get; } = [];
    public bool TimeOut { get; set; }
    public Action? OnStart { get; set; }

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
        OnStart?.Invoke();
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
