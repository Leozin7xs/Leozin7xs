using WPSupportTools.Core.Platform;
using WPSupportTools.Core.Procedures;

namespace WPSupportTools.Core.Modules.Printing.Correction.Procedures;

/// <summary>
/// Depois de religar o serviço de impressão, confere se a fila das impressoras tratadas ficou vazia.
/// Somente leitura. Se sobrou qualquer documento, a correção falha e o técnico é chamado (spooler ligado).
/// </summary>
public sealed class RecheckPrintQueueProcedure(PlatformServices platform) : IProcedure
{
    public string Id => "printing.recheck-queue";
    public string DisplayName => "Conferindo se a fila destravou";
    public bool RequiresElevation => false;

    public async Task<ProcedureResult> ExecuteAsync(ProcedureContext context, CancellationToken cancellationToken)
    {
        await Task.Delay(PrintingConstants.SpoolerSettleDelay, cancellationToken);

        var jobs = platform.PrintQueue.ReadJobs();
        var targets = PrintingState.GetTargets(context.State);
        context.Log($"Fila de impressão após reinício: {PrintQueueAnalyzer.Assess(jobs).Summary}");

        var remaining = targets is null
            ? PrintQueueAnalyzer.PrintersWithStuckJobs(jobs)
            : targets.Where(t => jobs.Any(j => string.Equals(j.PrinterName, t, StringComparison.OrdinalIgnoreCase))).ToList();

        return remaining.Count == 0
            ? ProcedureResult.Ok("Fila destravada.")
            : ProcedureResult.Fail($"A fila não foi limpa: ainda há documento em {string.Join(", ", remaining)}.");
    }
}
