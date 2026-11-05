using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using WPSupportTools.App.Elevation;
using WPSupportTools.App.Service;
using WPSupportTools.App.ViewModels;
using WPSupportTools.Core;
using WPSupportTools.Core.Execution;
using WPSupportTools.Core.Logging;
using WPSupportTools.Core.Service;
using WPSupportTools.Core.Telemetry;

namespace WPSupportTools.App;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private IAppLog? _log;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Modo administrador (iniciado pela própria interface via UAC): sem janela.
        if (WorkerArguments.IsWorkerInvocation(e.Args))
        {
            Shutdown(await ElevatedWorkerHost.RunAsync(e.Args));
            return;
        }

        // Instalação/remoção pelo técnico (iniciada pela própria interface via UAC): sem janela principal.
        if (e.Args is ["--install"] or ["--uninstall"])
        {
            Shutdown(RunInstaller(install: e.Args[0] == "--install"));
            return;
        }

        // Evita duas manutenções simultâneas no mesmo computador.
        _singleInstance = new Mutex(true, @"Local\WPSupportTools.UI", out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show("O WP Support Tools já está aberto.", AppInfo.Name,
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown(1);
            return;
        }

        _log = new FileAppLog();
        DispatcherUnhandledException += OnUnhandledException;
        new RunLogWriter(_log).ApplicationStarted();

        // Com o serviço instalado, as etapas de administrador não pedem senha; sem ele, usa o UAC.
        var viewModel = new MainViewModel(_log, new LocalSequenceExecutor(new ElevatedRunnerSelector(_log)),
            UsageReporter.CreateDefault(_log), UnitStore.Default);
        var window = new MainWindow(viewModel);
        window.Closed += (_, _) => Shutdown();
        window.Show();
    }

    /// <summary>Fecha esta cópia e abre a instalada em Program Files.</summary>
    public void OpenInstalledAndExit()
    {
        _singleInstance?.Dispose();
        _singleInstance = null;
        Process.Start(new ProcessStartInfo(ServiceConstants.InstalledExecutable) { UseShellExecute = true })?.Dispose();
        Shutdown();
    }

    private static int RunInstaller(bool install)
    {
        var log = new FileAppLog(ServiceConstants.ServiceLogDirectory, "instalacao");
        if (!Environment.IsPrivilegedProcess)
        {
            MessageBox.Show("A instalação precisa de permissão de administrador.", AppInfo.Name,
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return 2;
        }

        try
        {
            log.Write(LogLevel.Info, $"{(install ? "Instalação" : "Remoção")} iniciada ({AppInfo.Name} v{AppInfo.Version}).");
            if (install)
                Installer.Install(log);
            else
                Installer.Uninstall(log);
            return 0;
        }
        catch (Exception ex)
        {
            log.Write(LogLevel.Error, $"Falha: {ex.GetType().Name}: {ex.Message}");
            MessageBox.Show($"Não foi possível concluir.\n\n{ex.Message}", AppInfo.Name,
                MessageBoxButton.OK, MessageBoxImage.Error);
            return 1;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _log?.Write(LogLevel.Error, $"Erro não tratado: {e.Exception.GetType().Name}: {e.Exception.Message}");
        MessageBox.Show("Ocorreu um erro inesperado. Entre em contato com o suporte de TI.", AppInfo.Name,
            MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
