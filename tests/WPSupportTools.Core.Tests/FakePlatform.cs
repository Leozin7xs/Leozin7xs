using WPSupportTools.Core.Platform;

namespace WPSupportTools.Core.Tests;

/// <summary>Windows "de mentira" para testar as regras sem tocar no computador.</summary>
internal sealed class FakePlatform : IDisposable
{
    public FakeServices Services { get; } = new();
    public FakePrintQueue Queue { get; } = new();
    public List<PrintJobInfo> Jobs => Queue.Jobs;
    public FakeProcesses Processes { get; } = new();
    public DateTime Now { get; set; } = new(2026, 10, 20, 10, 0, 0);
    public List<InstalledPrinter> Printers { get; } = [];
    public FakePreferences Preferences { get; } = new();
    public FakeRawPrinter Raw { get; } = new();
    public string SpoolFolder { get; } = Path.Combine(Path.GetTempPath(), "wpst-spool-" + Guid.NewGuid().ToString("N"));
    public string? ConfiguredSpoolFolder { get; set; }
    public List<string> RegistryPrinters { get; } = [];

    /// <summary>Simula o Windows: ao religar o spooler, a fila passa a refletir os arquivos que sobraram na pasta.</summary>
    public bool QueueFollowsSpoolFolder { get; set; }

    public FakePlatform()
    {
        Directory.CreateDirectory(SpoolFolder);
        ConfiguredSpoolFolder = SpoolFolder;
        Modules.Printing.PrintingConstants.SpoolerSettleDelay = TimeSpan.Zero;
        Modules.Printing.Correction.Procedures.ClearSpoolFilesProcedure.LockedFileGracePeriod = TimeSpan.Zero;
        Services.OnStart = () =>
        {
            Queue.Unreadable = false;
            Queue.ReloadAfterRestart();
            if (QueueFollowsSpoolFolder)
                RebuildQueueFromSpoolFolder();
        };
    }

    public PlatformServices Build() =>
        new(Services, Queue, Processes, () => Now, new FakeCatalog(Printers), Preferences, Raw, new FakeSpool(this));

    /// <summary>Cria um documento na pasta da fila (.SHD com o nome da impressora + .SPL).</summary>
    public void AddSpoolJob(string printer, int number)
    {
        var shd = new List<byte>(new byte[] { 0x4B, 0x49, 0, 0, 0x10, 0, 0, 0 });
        shd.AddRange(System.Text.Encoding.Unicode.GetBytes("usuario\0" + printer + "\0" + "documento.pdf\0"));
        File.WriteAllBytes(SpoolPath($"{number:00000}.SHD"), [.. shd]);
        File.WriteAllText(SpoolPath($"{number:00000}.SPL"), "conteudo");
        if (!RegistryPrinters.Contains(printer))
            RegistryPrinters.Add(printer);
    }

    public string SpoolPath(string name) => Path.Combine(SpoolFolder, name);

    public string[] SpoolFiles() => Directory.GetFiles(SpoolFolder).Select(Path.GetFileName).Order().ToArray()!;

    private void RebuildQueueFromSpoolFolder()
    {
        Jobs.Clear();
        foreach (var shd in Directory.GetFiles(SpoolFolder, "*.SHD"))
        {
            var printer = Modules.Printing.Correction.SpoolFileAttribution.PrinterOf(File.ReadAllBytes(shd), RegistryPrinters);
            if (printer is not null)
                Jobs.Add(new PrintJobInfo(printer, PrintJobStatus.Error, Now));
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(SpoolFolder))
            Directory.Delete(SpoolFolder, recursive: true);
    }

    private sealed class FakeSpool(FakePlatform owner) : ISpoolFolder
    {
        public string StandardPath => owner.SpoolFolder;
        public string? ConfiguredPath() => owner.ConfiguredSpoolFolder;
        public IReadOnlyList<string> InstalledPrinterNames() => owner.RegistryPrinters;
    }

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

    /// <summary>Simula a fila "bugada": nem dá para ler nem cancelar os documentos.</summary>
    public bool Unreadable { get; set; }

    public IReadOnlyList<PrintJobInfo> ReadJobs() =>
        Unreadable ? throw new InvalidOperationException("Tempo esgotado ao consultar a fila.") : Jobs.ToList();

    public bool CancelAllJobs(string printerName)
    {
        if (Unreadable)
            throw new InvalidOperationException("Tempo esgotado ao consultar a fila.");
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
