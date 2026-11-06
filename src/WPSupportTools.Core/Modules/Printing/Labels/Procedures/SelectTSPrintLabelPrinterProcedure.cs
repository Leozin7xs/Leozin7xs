using WPSupportTools.Core.Platform;
using WPSupportTools.Core.Procedures;

namespace WPSupportTools.Core.Modules.Printing.Labels.Procedures;

/// <summary>
/// Garante que o TSPrint imprime as etiquetas na Zebra localizada: se a pasta de etiquetas ("etiq")
/// aponta para outra impressora, troca só esse nome no arquivo de impressoras, fechando o TSPrint antes
/// (para ele não regravar o arquivo antigo) e reabrindo depois.
/// </summary>
public sealed class SelectTSPrintLabelPrinterProcedure(PlatformServices platform, string? executablePath) : IProcedure
{
    public SelectTSPrintLabelPrinterProcedure(PlatformServices platform)
        : this(platform, PrintingConstants.TSPrintExecutablePath)
    {
    }

    public string Id => "labels.tsprint-printer";
    public string DisplayName => "Conferindo a etiquetadora no TSPrint";
    public bool RequiresElevation => false;

    public TimeSpan Timeout =>
        PrintingConstants.TSPrintCloseTimeout + PrintingConstants.TSPrintStartTimeout + TimeSpan.FromSeconds(10);

    public async Task<ProcedureResult> ExecuteAsync(ProcedureContext context, CancellationToken cancellationToken)
    {
        var printer = context.State.GetText(LabelState.PrinterName);
        if (printer is null)
            return ProcedureResult.NotNeeded("Etiquetadora não localizada.");

        string? config;
        try
        {
            config = platform.TSPrintConfig.ReadPrinterConfig();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ProcedureResult.Fail("Não foi possível ler a configuração do TSPrint.", ex.Message);
        }

        if (config is null)
            return ProcedureResult.NotNeeded("TSPrint não encontrado neste computador.");

        var current = TSPrintLabelPrinter.From(config);
        if (current is not null && string.Equals(current, printer.Trim(), StringComparison.OrdinalIgnoreCase))
            return ProcedureResult.Ok($"O TSPrint já imprime as etiquetas na {printer}.");

        var updated = TSPrintLabelPrinter.WithPrinter(config, printer);
        if (updated is null)
            return ProcedureResult.Fail(
                "O TSPrint não tem a pasta de etiquetas (\"etiq\") configurada. Chame o suporte para configurar o TSPrint.");

        context.Log($"TSPrint: etiquetas iam para '{current ?? "(nenhuma)"}'; trocando para '{printer}'.");

        var running = platform.Processes.FindInCurrentSession(PrintingConstants.TSPrintProcessName);
        foreach (var process in running)
        {
            if (!platform.Processes.Close(process.Id, PrintingConstants.TSPrintCloseTimeout))
                return ProcedureResult.Fail(
                    "Não foi possível fechar o TSPrint para trocar a etiquetadora. Feche o TSPrint e tente de novo.");
        }
        if (running.Count > 0)
            context.Log("TSPrint: fechado para gravar a configuração.");

        try
        {
            platform.TSPrintConfig.WritePrinterConfig(updated);
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
            return ProcedureResult.Ok($"TSPrint: as etiquetas agora vão para {printer}.");
        }

        var reopened = await ReopenAsync(cancellationToken);
        context.State.Set(PrintingState.TSPrintRunning, reopened);
        return reopened
            ? ProcedureResult.Ok($"TSPrint: as etiquetas agora vão para {printer}. TSPrint reaberto.")
            : ProcedureResult.Fail($"As etiquetas agora vão para {printer}, mas o TSPrint não reabriu. Abra o TSPrint manualmente.");
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
