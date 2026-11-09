using WPSupportTools.Core.Modules.Unilab;
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

        if (UnilabCloud.IsInstalled(platform))
            return await UnilabCloudAsync(context, printer, cancellationToken);

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
        {
            // Sem arquivo de impressoras mas com o TSPrint configurado: começa uma lista vazia.
            if (platform.TSPrintConfig.ReadGeneralConfig() is null)
                return ProcedureResult.NotNeeded("TSPrint não encontrado neste computador.");
            config = "[]";
        }

        var current = TSPrintLabelPrinter.From(config);
        if (current is not null && string.Equals(current, printer.Trim(), StringComparison.OrdinalIgnoreCase))
            return ProcedureResult.Ok($"O TSPrint já imprime as etiquetas na {printer}.");

        var updated = TSPrintLabelPrinter.WithPrinter(config, printer);
        if (updated is null && !TSPrintFolderPrinter.HasFolder(config, PrintingConstants.TSPrintLabelFolders))
        {
            context.Log("TSPrint: sem pasta de etiquetas; criando a pasta \"etiq\".");
            updated = TSPrintFolderPrinter.WithFolderAdded(config, PrintingConstants.TSPrintLabelFolders[0], printer);
        }
        if (updated is null)
            return ProcedureResult.Fail(
                "A configuração de impressoras do TSPrint não está num formato reconhecido. Chame o suporte.");

        context.Log($"TSPrint: etiquetas iam para '{current ?? "(nenhuma)"}'; trocando para '{printer}'.");
        return await new TSPrintPrinterSwitch(platform, executablePath)
            .ApplyAsync(context, updated, "as etiquetas", printer, cancellationToken);
    }

    /// <summary>UniLab novo: a pasta "etiq" fica no TSPrint embutido (config.json).</summary>
    private async Task<ProcedureResult> UnilabCloudAsync(ProcedureContext context, string printer, CancellationToken cancellationToken)
    {
        string? config;
        try
        {
            config = platform.Unilab.ReadCloudConfig();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ProcedureResult.Fail("Não foi possível ler a configuração do UniLab.", ex.Message);
        }

        if (UnilabCloud.LicenseOf(config) is null)
            return ProcedureResult.NotNeeded("O UniLab novo está sem licença (etapa anterior).");

        var current = UnilabCloud.PrinterOf(config, PrintingConstants.TSPrintLabelFolders);
        if (current is not null && string.Equals(current, printer.Trim(), StringComparison.OrdinalIgnoreCase))
            return ProcedureResult.Ok($"O TSPrint do UniLab já imprime as etiquetas na {printer}.");

        if (UnilabCloud.Apply(config, null, null, [new("etiq", printer, Print: true)]) is not { } result)
            return ProcedureResult.Fail("A configuração do UniLab não está num formato reconhecido. Chame o suporte.");

        context.Log($"TSPrint do UniLab: etiquetas iam para '{current ?? "(nenhuma)"}'; trocando para '{printer}'.");
        return await TSPrintPrinterSwitch.ForUnilabCloud(platform).ApplyAsync(
            context, () => platform.Unilab.WriteCloudConfig(result.Json),
            $"TSPrint do UniLab: as etiquetas agora vão para {printer}", cancellationToken);
    }
}
