using WPSupportTools.Core.Platform;
using WPSupportTools.Core.Procedures;

namespace WPSupportTools.Core.Modules.Printing;

/// <summary>
/// Troca a impressora de uma pasta do TSPrint: fecha o TSPrint (para ele não regravar o arquivo antigo),
/// grava só o nome da impressora dessa pasta e reabre o TSPrint se ele estava aberto.
/// </summary>
internal sealed class TSPrintPrinterSwitch(PlatformServices platform, string? executablePath)
{
    public static TimeSpan MaxDuration =>
        PrintingConstants.TSPrintCloseTimeout + PrintingConstants.TSPrintStartTimeout + TimeSpan.FromSeconds(10);

    /// <param name="what">Ex.: "as etiquetas", "os documentos A4".</param>
    public async Task<ProcedureResult> ApplyAsync(
        ProcedureContext context, string updatedConfig, string what, string printer, CancellationToken cancellationToken)
    {
        var running = platform.Processes.FindInCurrentSession(PrintingConstants.TSPrintProcessName);
        foreach (var process in running)
        {
            if (!platform.Processes.Close(process.Id, PrintingConstants.TSPrintCloseTimeout))
                return ProcedureResult.Fail(
                    $"Não foi possível fechar o TSPrint para trocar a impressora d{what[1..]}. Feche o TSPrint e tente de novo.");
        }
        if (running.Count > 0)
            context.Log("TSPrint: fechado para gravar a configuração.");

        try
        {
            platform.TSPrintConfig.WritePrinterConfig(updatedConfig);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (running.Count > 0)
                await ReopenAsync(cancellationToken);
            return ProcedureResult.Fail("Não foi possível gravar a configuração do TSPrint. Nada foi alterado.", ex.Message);
        }
        context.Log("TSPrint: configuração gravada (cópia da anterior em %LOCALAPPDATA%\\WPSupportTools\\Backups).");

        if (running.Count == 0)
        {
            context.State.Set(PrintingState.TSPrintRunning, false);
            return ProcedureResult.Ok($"TSPrint: {what} agora vão para {printer}.");
        }

        var reopened = await ReopenAsync(cancellationToken);
        context.State.Set(PrintingState.TSPrintRunning, reopened);
        return reopened
            ? ProcedureResult.Ok($"TSPrint: {what} agora vão para {printer}. TSPrint reaberto.")
            : ProcedureResult.Fail($"TSPrint: {what} agora vão para {printer}, mas o TSPrint não reabriu. Abra o TSPrint manualmente.");
    }

    private async Task<bool> ReopenAsync(CancellationToken cancellationToken)
    {
        if (executablePath is null || !platform.Processes.FileExists(executablePath))
            return false;

        platform.Processes.Start(executablePath);

        var deadline = platform.Now() + PrintingConstants.TSPrintStartTimeout;
        while (platform.Now() < deadline)
        {
            if (platform.Processes.FindInCurrentSession(PrintingConstants.TSPrintProcessName).Count > 0)
                return true;

            await Task.Delay(500, cancellationToken);
        }
        return false;
    }
}
