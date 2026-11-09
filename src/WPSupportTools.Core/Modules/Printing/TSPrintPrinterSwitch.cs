using WPSupportTools.Core.Platform;
using WPSupportTools.Core.Procedures;

namespace WPSupportTools.Core.Modules.Printing;

/// <summary>
/// Altera a configuração do TSPrint com segurança: fecha o TSPrint (para ele não regravar o arquivo antigo),
/// grava e reabre o TSPrint se ele estava aberto.
/// </summary>
internal sealed class TSPrintPrinterSwitch(PlatformServices platform, string? executablePath)
{
    public static TimeSpan MaxDuration =>
        PrintingConstants.TSPrintCloseTimeout + PrintingConstants.TSPrintStartTimeout + TimeSpan.FromSeconds(10);

    /// <summary>Troca a impressora no arquivo de impressoras. <paramref name="what"/>: "as etiquetas", "os documentos A4".</summary>
    public Task<ProcedureResult> ApplyAsync(
        ProcedureContext context, string updatedConfig, string what, string printer, CancellationToken cancellationToken) =>
        ApplyAsync(context, () => platform.TSPrintConfig.WritePrinterConfig(updatedConfig),
            $"TSPrint: {what} agora vão para {printer}", cancellationToken);

    /// <param name="write">Grava a configuração (com o TSPrint fechado).</param>
    /// <param name="done">Mensagem de sucesso, sem ponto final.</param>
    public async Task<ProcedureResult> ApplyAsync(
        ProcedureContext context, Action write, string done, CancellationToken cancellationToken)
    {
        var running = platform.Processes.FindInCurrentSession(PrintingConstants.TSPrintProcessName);
        foreach (var process in running)
        {
            if (!platform.Processes.Close(process.Id, PrintingConstants.TSPrintCloseTimeout))
                return ProcedureResult.Fail(
                    "Não foi possível fechar o TSPrint para alterar a configuração. Feche o TSPrint e tente de novo.");
        }
        if (running.Count > 0)
            context.Log("TSPrint: fechado para gravar a configuração.");

        try
        {
            write();
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
            return ProcedureResult.Ok($"{done}.");
        }

        var reopened = await ReopenAsync(cancellationToken);
        context.State.Set(PrintingState.TSPrintRunning, reopened);
        return reopened
            ? ProcedureResult.Ok($"{done}. TSPrint reaberto.")
            : ProcedureResult.Fail($"{done}, mas o TSPrint não reabriu. Abra o TSPrint manualmente.");
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
