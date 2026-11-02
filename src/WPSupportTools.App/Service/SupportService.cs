using System.ServiceProcess;
using WPSupportTools.Core;
using WPSupportTools.Core.Logging;
using WPSupportTools.Core.Service;

namespace WPSupportTools.App.Service;

/// <summary>
/// Serviço do Windows "WP Support Tools" (roda como sistema, instalado pelo técnico):
/// <list type="bullet">
///   <item>executa as etapas de administrador pedidas pela interface, sem senha;</item>
///   <item>mantém o programa atualizado com a versão aprovada para as unidades.</item>
/// </list>
/// Um único trabalho por vez: atualização nunca acontece no meio de uma manutenção.
/// </summary>
internal sealed class SupportService : ServiceBase
{
    private readonly FileAppLog _log = new(ServiceConstants.ServiceLogDirectory, "servico");
    private readonly SemaphoreSlim _maintenance = new(1, 1);
    private CancellationTokenSource? _cts;
    private Task? _pipeServer;
    private Task? _updater;

    public SupportService()
    {
        ServiceName = ServiceConstants.ServiceName;
        CanStop = true;
        CanShutdown = true;
    }

    protected override void OnStart(string[] args)
    {
        _log.Write(LogLevel.Info, $"Serviço iniciado ({AppInfo.Name} v{AppInfo.Version}).");
        ServiceUpdater.RemoveLeftovers(_log);

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _pipeServer = Task.Run(() => new ServicePipeServer(_log, _maintenance).RunAsync(token), token);
        _updater = Task.Run(() => new ServiceUpdater(_log, _maintenance).RunAsync(token), token);
    }

    protected override void OnStop() => StopWork("Serviço parado.");

    protected override void OnShutdown() => StopWork("Serviço parado (desligamento do Windows).");

    private void StopWork(string message)
    {
        _cts?.Cancel();
        try
        {
            Task.WaitAll([_pipeServer ?? Task.CompletedTask, _updater ?? Task.CompletedTask], TimeSpan.FromSeconds(10));
        }
        catch (AggregateException)
        {
            // Tarefas canceladas: esperado.
        }

        _log.Write(LogLevel.Info, message);
    }
}
