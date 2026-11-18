using WPSupportTools.Core.Platform;
using WPSupportTools.Core.Procedures;

namespace WPSupportTools.Core.Modules.Printing.Correction.Procedures;

/// <summary>
/// Igual a abrir a fila e "Cancelar todos os documentos", somente nas impressoras definidas na
/// verificação da fila (<see cref="PrintingState.TargetPrinters"/>). As outras filas não são tocadas.
/// O cancelamento é feito pelo próprio serviço de impressão.
/// Se a fila estiver travada a ponto de não deixar cancelar, a etapa não interrompe o processo:
/// como o técnico faz à mão, segue para parar o serviço e limpar os arquivos da fila.
/// Os números dos documentos cancelados ficam guardados para a limpeza da pasta da fila
/// (<see cref="PrintingState.TargetJobIds"/>).
/// </summary>
/// <param name="afterRestart">
/// Segunda passagem, depois de religar o spooler: documentos que estavam "excluindo" podem voltar
/// quando o serviço recarrega a fila; aqui eles são cancelados de novo. Se mesmo assim sobrar documento,
/// faz como o técnico (que reinicia de novo pelo services.msc): para o serviço, limpa os arquivos desses
/// documentos, religa e cancela mais uma vez. A conferência seguinte diz se a fila ficou vazia.
/// </param>
public sealed class CancelStuckJobsProcedure(PlatformServices platform, bool afterRestart = false) : IProcedure
{
    public string Id => afterRestart ? "printing.cancel-remaining-jobs" : "printing.cancel-stuck-jobs";
    public string DisplayName => afterRestart ? "Cancelando documentos restantes" : "Cancelando documentos travados";
    public bool RequiresElevation => true;

    /// <summary>Na segunda passagem pode haver mais um reinício do serviço (até 30 s para parar e 30 s para iniciar).</summary>
    public TimeSpan Timeout => afterRestart ? TimeSpan.FromMinutes(3) : TimeSpan.FromMinutes(2);

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

        var result = TryCancel(context);
        if (!afterRestart)
            return result;

        await Task.Delay(PrintingConstants.SpoolerSettleDelay, cancellationToken);
        var remaining = PrintersWithJobs(context, out var readable);
        if (!readable || remaining.Count == 0)
            return result;

        return await RestartAgainAsync(context, remaining, cancellationToken);
    }

    /// <summary>Segundo reinício do serviço, como o técnico faz quando o documento não sai nem depois do primeiro.</summary>
    private async Task<ProcedureResult> RestartAgainAsync(
        ProcedureContext context, IReadOnlyList<string> remaining, CancellationToken cancellationToken)
    {
        context.Log($"Ainda há documento em {string.Join(", ", remaining)} depois de cancelar. " +
                    "Reiniciando o serviço de impressão mais uma vez e limpando os arquivos desses documentos.");

        var stop = await new StopPrintSpoolerProcedure(platform).ExecuteAsync(context, cancellationToken);
        context.Log($"Parar o serviço (2ª vez): {stop.Message}");
        ProcedureResult? clear = null;
        if (stop.IsSuccessful)
        {
            clear = await new ClearSpoolFilesProcedure(platform).ExecuteAsync(context, cancellationToken);
            context.Log($"Limpar arquivos da fila (2ª vez): {clear.Message}");
        }

        // O serviço nunca pode ficar desligado, mesmo que a limpeza falhe.
        var start = await new StartPrintSpoolerProcedure(platform).ExecuteAsync(context, CancellationToken.None);
        context.Log($"Iniciar o serviço (2ª vez): {start.Message}");

        if (!stop.IsSuccessful)
            return stop;
        if (!start.IsSuccessful)
            return start;
        if (clear is { IsSuccessful: false })
            return clear;

        await Task.Delay(PrintingConstants.SpoolerSettleDelay, cancellationToken);
        var again = TryCancel(context);
        return again.IsSuccessful
            ? ProcedureResult.Ok("Documentos cancelados; o serviço de impressão foi reiniciado mais uma vez.")
            : again;
    }

    private ProcedureResult TryCancel(ProcedureContext context)
    {
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
        var printers = Targeted(context, jobs);

        if (printers.Count == 0)
            return ProcedureResult.Ok(afterRestart ? "Nenhum documento restante." : "Nenhum documento travado no momento.");

        PrintingState.AddJobIds(context.State, jobs
            .Where(j => printers.Contains(j.PrinterName, StringComparer.OrdinalIgnoreCase))
            .Select(j => j.JobId)
            .OfType<uint>());

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

    /// <summary>Impressoras tratadas que ainda têm documento na fila (false em <paramref name="readable"/> se a fila não respondeu).</summary>
    private IReadOnlyList<string> PrintersWithJobs(ProcedureContext context, out bool readable)
    {
        try
        {
            var jobs = platform.PrintQueue.ReadJobs();
            readable = true;
            return Targeted(context, jobs);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            context.Log($"Não foi possível conferir a fila depois de cancelar ({ex.GetType().Name}: {ex.Message}).");
            readable = false;
            return [];
        }
    }

    private static IReadOnlyList<string> Targeted(ProcedureContext context, IReadOnlyList<PrintJobInfo> jobs)
    {
        var targets = PrintingState.GetTargets(context.State);
        return targets is null
            ? PrintQueueAnalyzer.PrintersWithStuckJobs(jobs)
            : targets.Where(t => jobs.Any(j => string.Equals(j.PrinterName, t, StringComparison.OrdinalIgnoreCase))).ToList();
    }
}
