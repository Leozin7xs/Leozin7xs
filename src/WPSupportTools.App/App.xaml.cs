using System.Windows;
using System.Windows.Threading;
using WPSupportTools.App.Elevation;
using WPSupportTools.App.ViewModels;
using WPSupportTools.Core;
using WPSupportTools.Core.Execution;
using WPSupportTools.Core.Logging;

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

        var viewModel = new MainViewModel(_log, new InProcessSequenceExecutor(), new ElevatedWorkerExecutor());
        var window = new MainWindow(viewModel);
        window.Closed += (_, _) => Shutdown();
        window.Show();
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
