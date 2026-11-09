using WPSupportTools.Core.Platform;
using WPSupportTools.Core.Procedures;

namespace WPSupportTools.Core.Modules.Printing.Correction.Procedures;

/// <summary>
/// Verifica se o TSPrint está aberto na sessão do usuário. Somente leitura.
/// </summary>
/// <remarks>
/// Limitação atual: confirma que o programa está aberto, mas ainda não lê o estado interno
/// ("Em Execução" / "Parado") exibido na tela do TSPrint.
/// </remarks>
public sealed class CheckTSPrintProcedure(PlatformServices platform) : IProcedure
{
    public string Id => "printing.check-tsprint";
    public string DisplayName => "Verificando o TSPrint";
    public bool RequiresElevation => false;

    public Task<ProcedureResult> ExecuteAsync(ProcedureContext context, CancellationToken cancellationToken)
    {
        if (Unilab.UnilabCloud.IsInstalled(platform))
        {
            // UniLab novo: o TSPrint é o embutido nele.
            var open = platform.Processes.FindInCurrentSession(Unilab.UnilabCloud.ProcessName)
                .Any(p => Unilab.ConfigureUnilabProcedure.IsCloud(p.Path));
            context.State.Set(PrintingState.TSPrintRunning, open);
            return Task.FromResult(ProcedureResult.Ok(open ? "O UniLab (com o TSPrint) está aberto." : "O UniLab não está aberto."));
        }

        var running = platform.Processes.FindInCurrentSession(PrintingConstants.TSPrintProcessName);
        context.State.Set(PrintingState.TSPrintRunning, running.Count > 0);

        if (running.Count == 0)
        {
            context.Log("TSPrint: não está aberto nesta sessão.");
            return Task.FromResult(ProcedureResult.Ok("O TSPrint não está aberto."));
        }

        var first = running[0];
        context.Log($"TSPrint: aberto ({running.Count} processo(s); {first.ProductName ?? "?"} {first.FileVersion ?? ""}).");
        return Task.FromResult(ProcedureResult.Ok("O TSPrint está aberto."));
    }
}
