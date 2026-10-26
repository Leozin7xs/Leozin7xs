using WPSupportTools.Core.Platform;
using WPSupportTools.Core.Procedures;

namespace WPSupportTools.Core.Modules.Printing.Correction.Procedures;

/// <summary>
/// Depois de reiniciar o serviço de impressão, confere se ainda sobrou documento travado.
/// Somente leitura. Se sobrou, a correção falha e o técnico é chamado (com o spooler ligado).
/// </summary>
public sealed class RecheckPrintQueueProcedure(PlatformServices platform) : IProcedure
{
    public string Id => "printing.recheck-queue";
    public string DisplayName => "Conferindo se a fila destravou";
    public bool RequiresElevation => false;

    public Task<ProcedureResult> ExecuteAsync(ProcedureContext context, CancellationToken cancellationToken)
    {
        var jobs = platform.PrintQueue.ReadJobs();
        context.Log($"Fila de impressão após reinício: {PrintQueueAnalyzer.Assess(jobs).Summary}");

        var stuck = PrintQueueAnalyzer.PrintersWithStuckJobs(jobs);
        return Task.FromResult(stuck.Count == 0
            ? ProcedureResult.Ok("Fila destravada.")
            : ProcedureResult.Fail($"Ainda há documento travado em: {string.Join(", ", stuck)}."));
    }
}
