using WPSupportTools.Core.Platform;
using WPSupportTools.Core.Procedures;

namespace WPSupportTools.Core.Modules.Printing.Labels.Procedures;

/// <summary>Localiza a etiquetadora Zebra e confere se está ligada. Somente leitura.</summary>
public sealed class LocateLabelPrinterProcedure(PlatformServices platform) : IProcedure
{
    public string Id => "labels.locate";
    public string DisplayName => "Localizando a etiquetadora";
    public bool RequiresElevation => false;

    public Task<ProcedureResult> ExecuteAsync(ProcedureContext context, CancellationToken cancellationToken)
    {
        var printers = platform.Printers.List();
        foreach (var zebra in printers.Where(p => ZebraModel.LanguageOf(p.DriverName) is not null))
            context.Log($"Zebra instalada: '{zebra.Name}' (driver {zebra.DriverName}, porta {zebra.PortName}, " +
                        $"{(zebra.IsOffline ? "offline" : "online")}{(zebra.IsDefault ? ", padrão" : "")}).");

        var tsprintLabelPrinter = ReadTSPrintLabelPrinter(context);

        var choice = LabelPrinterSelector.Select(printers, tsprintLabelPrinter);
        if (choice.Printer is null || choice.Language is null)
            return Task.FromResult(ProcedureResult.Fail(choice.Message));

        context.Log(choice.Message);
        context.State.SetText(LabelState.PrinterName, choice.Printer.Name);
        context.State.SetText(LabelState.Language, choice.Language.Value.ToString());
        return Task.FromResult(ProcedureResult.Ok($"{choice.Printer.Name} ({choice.Language.Value.ToString().ToUpperInvariant()})."));
    }

    /// <summary>Etiquetadora configurada no TSPrint. Qualquer problema na leitura só é registrado (não é falha).</summary>
    private string? ReadTSPrintLabelPrinter(ProcedureContext context)
    {
        try
        {
            var config = platform.TSPrintConfig.ReadPrinterConfig();
            if (config is null)
            {
                context.Log("TSPrint: arquivo de impressoras não encontrado.");
                return null;
            }

            var name = TSPrintLabelPrinter.From(config);
            context.Log(name is null
                ? "TSPrint: nenhuma impressora de etiquetas configurada (ou formato não reconhecido)."
                : $"TSPrint: etiquetas vão para '{name}'.");
            return name;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            context.Log($"TSPrint: não foi possível ler o arquivo de impressoras ({ex.Message}).");
            return null;
        }
    }
}
