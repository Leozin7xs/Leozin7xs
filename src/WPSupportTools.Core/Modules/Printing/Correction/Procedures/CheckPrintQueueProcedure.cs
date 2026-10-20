using WPSupportTools.Core.Platform;
using WPSupportTools.Core.Procedures;

namespace WPSupportTools.Core.Modules.Printing.Correction.Procedures;

/// <summary>
/// Passo 1 do procedimento WP: ver se a fila de impressão está travada. Somente leitura.
/// Se o serviço de impressão estiver parado, também segue pelo caminho de reinício.
/// </summary>
public sealed class CheckPrintQueueProcedure(PlatformServices platform) : IProcedure
{
    public string Id => "printing.check-queue";
    public string DisplayName => "Verificando fila de impressão";
    public bool RequiresElevation => false;

    public Task<ProcedureResult> ExecuteAsync(ProcedureContext context, CancellationToken cancellationToken)
    {
        var spooler = platform.Services.Find(PrintingConstants.SpoolerServiceName);
        if (spooler is null)
            return Task.FromResult(ProcedureResult.Fail(
                "Serviço de impressão do Windows (Spooler) não encontrado neste computador."));

        context.Log($"Serviço {spooler.Name} ({spooler.DisplayName}): {spooler.State}.");

        if (spooler.State != ServiceState.Running)
        {
            context.State.Set(PrintingState.QueueNeedsReset, true);
            return Task.FromResult(ProcedureResult.Ok(
                "O serviço de impressão não está em execução. Ele será reiniciado."));
        }

        var assessment = PrintQueueAnalyzer.Assess(platform.PrintJobs.ReadJobs());
        context.Log($"Fila de impressão: {assessment.Summary}");
        context.State.Set(PrintingState.QueueNeedsReset, assessment.IsStuck);

        return Task.FromResult(ProcedureResult.Ok(assessment.IsStuck
            ? $"Fila travada ({assessment.JobsWithProblem} documento(s) com erro)."
            : assessment.TotalJobs == 0 ? "Fila sem documentos travados." : "Fila funcionando normalmente."));
    }
}
