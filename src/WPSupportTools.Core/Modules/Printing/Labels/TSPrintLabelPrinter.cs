namespace WPSupportTools.Core.Modules.Printing.Labels;

/// <summary>Impressora da pasta de etiquetas do TSPrint ("etiq", "etiqueta"...).</summary>
public static class TSPrintLabelPrinter
{
    public static bool IsLabelFolder(string? folder) =>
        TSPrintFolderPrinter.IsFolder(folder, PrintingConstants.TSPrintLabelFolders);

    /// <summary>Nome da impressora da pasta de etiquetas, ou null se não houver / o arquivo não for reconhecido.</summary>
    public static string? From(string? config) =>
        TSPrintFolderPrinter.PrinterOf(config, PrintingConstants.TSPrintLabelFolders);

    /// <summary>O mesmo arquivo com a impressora da pasta de etiquetas trocada (resto idêntico), ou null.</summary>
    public static string? WithPrinter(string? config, string printerName) =>
        TSPrintFolderPrinter.WithPrinter(config, PrintingConstants.TSPrintLabelFolders, printerName);
}
