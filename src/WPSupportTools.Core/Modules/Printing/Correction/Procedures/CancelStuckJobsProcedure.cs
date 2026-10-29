using WPSupportTools.Core.Platform;
using WPSupportTools.Core.Procedures;

namespace WPSupportTools.Core.Modules.Printing.Correction.Procedures;

/// <summary>
/// Igual a abrir a fila e "Cancelar todos os documentos", somente nas impressoras definidas na
/// verificação da fila (<see cref="PrintingState.TargetPrinters"/>). As outras filas não são tocadas.
/// O cancelamento é feito pelo próprio serviço de impressão.
/// Se a fila estiver travada a ponto de não deixar cancelar, a etapa não interrompe o processo:
/// como o técnico faz à mão, segue para parar o serviço e limpar os arquivos da fila.
/// </summary>
/// <param name="afterRestart">
/// Segunda passagem, depois de religar o spooler: documentos que estavam "excluindo" podem voltar
/// quando o serviço recarrega a fila; aqui eles são cancelados de novo.
/// </param>
public sealed class CancelStuckJobsProcedure(PlatformServices platform, bool afterRestart = false) : IProcedure
{
    public string Id => afterRestart ? "printing.cancel-remaining-jobs" : "printing.cancel-stuck-jobs";
    public string DisplayName => afterRestart ? "Cancelando documentos restantes" : "Cancelando documentos travados";
    public bool RequiresElevation => true;

    public async Task<ProcedureResult> ExecuteAsync(ProcedureContext context, CancellationToken cancellationToken)
    {
        var spooler = platform.Services.Find(PrintingConstants.SpoolerServiceName);
        if (spooler is null)
            return ProcedureResult.Fail("Serviço de impressão (Spooler) não encontrado. Nada foi alterado.");

        // Com o serviço parado não há fila para consultar; o reinício a seguir resolve esse caso.
        if (spooler.State != ServiceState.Running)
            return ProcedureResult.Ok("Serviço de impressão parado: não há fila para cancelar.");

        if (afterRestart)
            await Task.Delay(PrintingConstants.SpoolerSettleDelay, cancellationToken);

        try
        {
            return Cancel(context);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not UnauthorizedAccessException)
        {
            context.Log($"Não foi possível cancelar pela fila ({ex.GetType().Name}: {ex.Message}). Seguindo com a limpeza.");
            return ProcedureResult.Ok("Não foi possível cancelar pela fila; a limpeza continua com o serviço parado.");
        }
    }

    private ProcedureResult Cancel(ProcedureContext context)
    {
        var jobs = platform.PrintQueue.ReadJobs();
        var targets = PrintingState.GetTargets(context.State);
        var printers = targets is null
            ? PrintQueueAnalyzer.PrintersWithStuckJobs(jobs)
            : targets.Where(t => jobs.Any(j => string.Equals(j.PrinterName, t, StringComparison.OrdinalIgnoreCase))).ToList();

        if (printers.Count == 0)
            return ProcedureResult.Ok(afterRestart ? "Nenhum documento restante." : "Nenhum documento travado no momento.");

        var missing = new List<string>();
        foreach (var printer in printers)
        {
            if (platform.PrintQueue.CancelAllJobs(printer))
                context.Log($"Documentos cancelados na impressora '{printer}'.");
            else
                missing.Add(printer);
        }

        if (missing.Count > 0)
        {
            context.Log($"Impressora não encontrada para cancelar: {string.Join(", ", missing)}. Seguindo com a limpeza.");
            return ProcedureResult.Ok("Alguns documentos não puderam ser cancelados pela fila; a limpeza continua.");
        }

        return ProcedureResult.Ok($"Documentos cancelados em: {string.Join(", ", printers)}.");
    }
}
