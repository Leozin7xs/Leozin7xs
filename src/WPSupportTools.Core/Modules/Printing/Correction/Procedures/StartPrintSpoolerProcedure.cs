using WPSupportTools.Core.Platform;
using WPSupportTools.Core.Procedures;

namespace WPSupportTools.Core.Modules.Printing.Correction.Procedures;

/// <summary>
/// Equivalente a "net start spooler". Não altera o tipo de inicialização do serviço.
/// Roda mesmo se a limpeza da fila falhar: o spooler nunca pode ficar desligado.
/// </summary>
public sealed class StartPrintSpoolerProcedure(PlatformServices platform) : IProcedure
{
    public string Id => "printing.start-spooler";
    public string DisplayName => "Iniciando serviço de impressão";
    public bool RequiresElevation => true;
    public bool RunsEvenAfterFailure => true;

    public Task<ProcedureResult> ExecuteAsync(ProcedureContext context, CancellationToken cancellationToken)
    {
        var spooler = platform.Services.Find(PrintingConstants.SpoolerServiceName);
        if (spooler is null)
            return Task.FromResult(ProcedureResult.Fail("Serviço de impressão (Spooler) não encontrado. Nada foi alterado."));

        if (spooler.IsDisabled)
            return Task.FromResult(ProcedureResult.Fail(
                "O serviço de impressão está desativado no Windows. Nada foi alterado."));

        if (spooler.State == ServiceState.Running)
            return Task.FromResult(ProcedureResult.Ok("O serviço de impressão já estava em execução."));

        try
        {
            platform.Services.Start(spooler.Name, PrintingConstants.ServiceTimeout);
        }
        catch (TimeoutException)
        {
            return Task.FromResult(ProcedureResult.Fail(
                $"O serviço de impressão não iniciou em {PrintingConstants.ServiceTimeout.TotalSeconds:0} segundos."));
        }

        return Task.FromResult(ProcedureResult.Ok("Serviço de impressão iniciado."));
    }
}
