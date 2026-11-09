using WPSupportTools.Core.Modules.Printing.Labels;
using WPSupportTools.Core.Platform;

namespace WPSupportTools.Core.Modules.Printing.Correction;

/// <summary>
/// Decide em qual impressora o TSPrint deve imprimir os documentos A4 (pasta "root").
/// Só troca quando a configurada não existe ou está desligada/sem comunicação E existe exatamente uma outra
/// impressora física funcionando. Com mais de uma possível, não adivinha. Se a configurada só precisa de
/// atenção (sem papel, tampa aberta...), avisa e não troca.
/// </summary>
public static class DocumentPrinterSelector
{
    public sealed record Choice(InstalledPrinter? SwitchTo, string Message, bool AlreadyWorking = false, bool Ambiguous = false);

    private static readonly string[] VirtualPorts = ["PORTPROMPT:", "NUL:", "SHRFAX:", "FILE:", "XPSPORT:"];
    private static readonly string[] VirtualDrivers = ["Microsoft Print To PDF", "Microsoft XPS", "OneNote", "Fax", "Send to Microsoft"];

    /// <summary>Impressora de verdade (não PDF/XPS/OneNote/fax) e que não é etiquetadora.</summary>
    public static bool IsPhysicalDocumentPrinter(InstalledPrinter printer) =>
        ZebraModel.LanguageOf(printer.DriverName) is null &&
        !VirtualPorts.Contains(printer.PortName.Trim(), StringComparer.OrdinalIgnoreCase) &&
        !printer.PortName.StartsWith("Microsoft.Office.OneNote", StringComparison.OrdinalIgnoreCase) &&
        !VirtualDrivers.Any(v => printer.DriverName.Contains(v, StringComparison.OrdinalIgnoreCase));

    public static Choice Select(IReadOnlyList<InstalledPrinter> printers, string tsprintPrinter)
    {
        var candidates = printers.Where(IsPhysicalDocumentPrinter).ToList();
        var current = printers.FirstOrDefault(p => string.Equals(p.Name.Trim(), tsprintPrinter.Trim(), StringComparison.OrdinalIgnoreCase));

        if (current is not null && !current.IsOffline)
        {
            // A impressora certa só precisa de atenção (papel, tampa...): trocar não resolve.
            return current.Problem is null
                ? new Choice(null, $"O TSPrint já imprime os documentos na {current.Name}, que está funcionando.", AlreadyWorking: true)
                : new Choice(null, $"A impressora {current.Name} está {current.Problem}. Resolva na impressora e tente imprimir de novo.");
        }

        var problem = current is null
            ? $"A impressora do TSPrint ('{tsprintPrinter}') não está instalada neste computador"
            : $"A impressora do TSPrint ('{tsprintPrinter}') está desligada ou com erro";

        var working = candidates.Where(p => !p.IsOffline && p.Problem is null && !SameName(p, tsprintPrinter)).ToList();
        return working.Count switch
        {
            1 => new Choice(working[0], $"{problem}; trocando para '{working[0].Name}', a que está funcionando."),
            0 => new Choice(null,
                $"{problem} e nenhuma outra impressora está funcionando. Confira se a impressora está ligada e conectada."),
            _ => new Choice(null,
                $"{problem} e há mais de uma funcionando ({string.Join(", ", working.Select(p => $"'{p.Name}'"))}). " +
                "Chame o suporte para escolher a certa.", Ambiguous: true),
        };
    }

    /// <summary>O TSPrint não tem impressora A4: usa a única impressora física funcionando, se houver só uma.</summary>
    public static Choice SelectForEmptyTSPrint(IReadOnlyList<InstalledPrinter> printers)
    {
        var working = printers.Where(p => IsPhysicalDocumentPrinter(p) && !p.IsOffline && p.Problem is null).ToList();
        return working.Count switch
        {
            1 => new Choice(working[0], $"O TSPrint está sem impressora A4; usando '{working[0].Name}', a que está funcionando."),
            0 => new Choice(null,
                "O TSPrint está sem impressora A4 e nenhuma impressora está funcionando. Confira se a impressora está ligada e conectada."),
            _ => new Choice(null,
                $"O TSPrint está sem impressora A4 e há mais de uma funcionando ({string.Join(", ", working.Select(p => $"'{p.Name}'"))}). " +
                "Chame o suporte para escolher a certa.", Ambiguous: true),
        };
    }

    private static bool SameName(InstalledPrinter p, string name) =>
        string.Equals(p.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase);
}
