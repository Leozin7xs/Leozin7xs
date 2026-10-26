using WPSupportTools.Core.Platform;

namespace WPSupportTools.Core.Modules.Printing.Labels;

public sealed record LabelPrinterChoice(InstalledPrinter? Printer, ZebraLanguage? Language, string Message);

/// <summary>
/// Escolhe a etiquetadora Zebra deste computador, sem adivinhar:
/// <list type="bullet">
///   <item>Nenhuma Zebra instalada → não escolhe.</item>
///   <item>Todas offline → não escolhe e avisa que está desligada/desconectada.</item>
///   <item>Uma só ligada → essa.</item>
///   <item>Mais de uma ligada → a padrão do Windows, se for uma delas; senão não escolhe.</item>
/// </list>
/// </summary>
public static class LabelPrinterSelector
{
    public static LabelPrinterChoice Select(IReadOnlyList<InstalledPrinter> printers)
    {
        var zebras = printers.Where(p => ZebraModel.LanguageOf(p.DriverName) is not null).ToList();
        if (zebras.Count == 0)
            return new LabelPrinterChoice(null, null, "Nenhuma etiquetadora Zebra instalada neste computador.");

        var online = zebras.Where(p => !p.IsOffline).ToList();
        if (online.Count == 0)
            return new LabelPrinterChoice(null, null,
                "A etiquetadora está desligada ou desconectada. Verifique se ela está ligada e com o cabo conectado.");

        var chosen = online.Count == 1 ? online[0] : online.SingleOrDefault(p => p.IsDefault);
        if (chosen is null)
            return new LabelPrinterChoice(null, null,
                "Há mais de uma etiquetadora ativa neste computador: " +
                string.Join(", ", online.Select(p => p.Name)) + ". Chame o técnico para indicar qual é a usada.");

        return new LabelPrinterChoice(chosen, ZebraModel.LanguageOf(chosen.DriverName), $"Etiquetadora: {chosen.Name}.");
    }
}
