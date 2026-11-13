using WPSupportTools.Core.Platform;

namespace WPSupportTools.Core.Modules.Printing.Labels;

public sealed record LabelPrinterChoice(InstalledPrinter? Printer, ZebraLanguage? Language, string Message);

/// <summary>
/// Escolhe a etiquetadora Zebra deste computador, sem adivinhar:
/// <list type="bullet">
///   <item>Nenhuma Zebra instalada → não escolhe.</item>
///   <item>Todas offline → não escolhe e avisa que está desligada/desconectada.</item>
///   <item>Uma só ligada → essa.</item>
///   <item>Mais de uma ligada → a conectada por USB agora (trocar a impressora deixa a antiga instalada, e o
///         Windows cria "(Copiar 1)"...); senão, deixando de fora as de porta USB sem aparelho, a única que sobrar,
///         a configurada no TSPrint para etiquetas ou a padrão do Windows; senão não escolhe.</item>
/// </list>
/// </summary>
public static class LabelPrinterSelector
{
    /// <param name="tsprintLabelPrinter">Impressora de etiquetas configurada no TSPrint (null se não houver).</param>
    public static LabelPrinterChoice Select(IReadOnlyList<InstalledPrinter> printers, string? tsprintLabelPrinter = null)
    {
        var zebras = printers.Where(p => ZebraModel.LanguageOf(p.DriverName) is not null).ToList();
        if (zebras.Count == 0)
            return new LabelPrinterChoice(null, null, "Nenhuma etiquetadora Zebra instalada neste computador.");

        var online = zebras.Where(p => !p.IsOffline).ToList();
        if (online.Count == 0)
            return new LabelPrinterChoice(null, null,
                "A etiquetadora está desligada ou desconectada. Verifique se ela está ligada e com o cabo conectado.");

        if (online.Count == 1)
            return Chosen(online[0], "");

        var usb = online.Where(p => p.UsbConnected == true).ToList();
        if (usb.Count == 1)
            return Chosen(usb[0], " (conectada por USB)");

        // Várias conectadas por USB: decide entre elas. Nenhuma: fora as de porta USB sem aparelho (antigas).
        var candidates = usb.Count > 1 ? usb : online.Where(p => p.UsbConnected != false).ToList();
        if (candidates.Count == 0)
            candidates = online;
        if (candidates.Count == 1)
            return Chosen(candidates[0], "");

        var fromTSPrint = string.IsNullOrWhiteSpace(tsprintLabelPrinter)
            ? null
            : candidates.FirstOrDefault(p => string.Equals(p.Name.Trim(), tsprintLabelPrinter.Trim(), StringComparison.OrdinalIgnoreCase));
        if (fromTSPrint is not null)
            return Chosen(fromTSPrint, " (configurada no TSPrint)");

        var byDefault = candidates.SingleOrDefault(p => p.IsDefault);
        if (byDefault is not null)
            return Chosen(byDefault, " (padrão do Windows)");

        var names = string.Join(", ", candidates.Select(p => p.Name));
        return new LabelPrinterChoice(null, null, string.IsNullOrWhiteSpace(tsprintLabelPrinter)
            ? $"Há mais de uma etiquetadora ativa neste computador: {names}. " +
              "Não foi possível ver no TSPrint qual é a usada. Chame o técnico."
            : $"Há mais de uma etiquetadora ativa neste computador: {names}. " +
              $"O TSPrint está configurado para \"{tsprintLabelPrinter.Trim()}\", que não está entre elas. Chame o técnico.");
    }

    private static LabelPrinterChoice Chosen(InstalledPrinter printer, string reason) =>
        new(printer, ZebraModel.LanguageOf(printer.DriverName), $"Etiquetadora: {printer.Name}{reason}.");
}
