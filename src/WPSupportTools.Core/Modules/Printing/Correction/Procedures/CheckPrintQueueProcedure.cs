using WPSupportTools.Core.Platform;
using WPSupportTools.Core.Procedures;

namespace WPSupportTools.Core.Modules.Printing.Correction.Procedures;

/// <summary>
/// Passo 1 do procedimento WP: ver se a fila de impressão está travada. Somente leitura.
/// Se o serviço de impressão estiver parado, também segue pelo caminho de reinício.
/// Deixa registrado quais impressoras terão a fila limpa (<see cref="PrintingState.TargetPrinters"/>).
/// </summary>
/// <param name="onlyPrinter">Se informado, considera somente a fila desta impressora (ex.: a etiquetadora).</param>
/// <param name="anyPendingJobIsStuck">
/// Se verdadeiro, qualquer documento parado na fila conta como travado — usado quando a funcionária já
/// informou que "não sai nada". Se falso, só documentos com erro contam.
/// </param>
public sealed class CheckPrintQueueProcedure(
    PlatformServices platform, Func<RunState, string?>? onlyPrinter = null, bool anyPendingJobIsStuck = false)
    : IProcedure
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
        var printer = onlyPrinter?.Invoke(context.State);

        if (spooler.State != ServiceState.Running)
        {
            context.State.Set(PrintingState.QueueNeedsReset, true);
            if (printer is not null)
                PrintingState.SetTargets(context.State, [printer]);
            return Task.FromResult(ProcedureResult.Ok(
                "O serviço de impressão não está em execução. Ele será reiniciado."));
        }

        var jobs = platform.PrintQueue.ReadJobs();
        if (printer is not null)
            jobs = jobs.Where(j => string.Equals(j.PrinterName, printer, StringComparison.OrdinalIgnoreCase)).ToList();

        var assessment = PrintQueueAnalyzer.Assess(jobs);
        context.Log($"Fila de impressão{(printer is null ? "" : $" ({printer})")}: {assessment.Summary}");

        var stuckPrinters = anyPendingJobIsStuck
            ? jobs.Select(j => j.PrinterName).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            : PrintQueueAnalyzer.PrintersWithStuckJobs(jobs);

        context.State.Set(PrintingState.QueueNeedsReset, stuckPrinters.Count > 0);
        if (stuckPrinters.Count > 0)
            PrintingState.SetTargets(context.State, stuckPrinters);

        return Task.FromResult(ProcedureResult.Ok(stuckPrinters.Count > 0
            ? $"Fila travada ({jobs.Count(j => stuckPrinters.Contains(j.PrinterName, StringComparer.OrdinalIgnoreCase))} documento(s) parado(s))."
            : assessment.TotalJobs == 0 ? "Fila sem documentos travados." : "Fila funcionando normalmente."));
    }
}
