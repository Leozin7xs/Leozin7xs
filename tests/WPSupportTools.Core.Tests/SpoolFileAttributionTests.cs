using System.Text;
using WPSupportTools.Core.Modules.Printing.Correction;

namespace WPSupportTools.Core.Tests;

public class SpoolFileAttributionTests
{
    private static byte[] Shadow(params string[] texts) =>
        [0x4B, 0x49, 0, 0, .. Encoding.Unicode.GetBytes(string.Concat(texts.Select(t => t + "\0")))];

    private static readonly string[] Printers =
        ["ZDesigner TLP 2844", "ZDesigner TLP 2844 (Copiar 2)", "Brother DCP-T820DW Printer"];

    [Fact]
    public void Finds_the_printer_of_the_document() =>
        Assert.Equal("Brother DCP-T820DW Printer",
            SpoolFileAttribution.PrinterOf(Shadow("maria", "Brother DCP-T820DW Printer", "laudo.pdf"), Printers));

    [Fact]
    public void Does_not_confuse_a_copy_with_the_original() =>
        Assert.Equal("ZDesigner TLP 2844 (Copiar 2)",
            SpoolFileAttribution.PrinterOf(Shadow("ZDesigner TLP 2844 (Copiar 2)", "etiqueta"), Printers));

    [Fact]
    public void Unknown_printer_is_not_guessed() =>
        Assert.Null(SpoolFileAttribution.PrinterOf(Shadow("Outra impressora", "doc"), Printers));

    [Fact]
    public void Two_different_printers_in_the_same_file_are_ambiguous() =>
        Assert.Null(SpoolFileAttribution.PrinterOf(Shadow("ZDesigner TLP 2844", "Brother DCP-T820DW Printer"), Printers));
}
