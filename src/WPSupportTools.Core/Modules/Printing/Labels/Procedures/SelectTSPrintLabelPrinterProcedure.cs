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
    public TimeSpan Timeout => TSPrintPrinterSwitch.MaxDuration;

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
        return await new TSPrintPrinterSwitch(platform, executablePath)
            .ApplyAsync(context, updated, "as etiquetas", printer, cancellationToken);
    }
}
