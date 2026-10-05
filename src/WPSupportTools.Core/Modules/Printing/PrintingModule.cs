using WPSupportTools.Core.Modules.Printing.Correction;

namespace WPSupportTools.Core.Modules.Printing;

public static class PrintingModule
{
    public static SupportModule Definition { get; } = new(
        Id: "printing",
        DisplayName: "Impressão",
        Actions:
        [
            new SupportAction(
                Title: "Corrigir problema de impressão",
                Description: "Esta ferramenta executará procedimentos automáticos para tentar corrigir problemas comuns de impressão.",
                Kind: SupportActionKind.Correcao,
                SequenceId: PrintingFixSequence.Id,
                IconGlyph: ""),
            // Diagnóstico de impressão: ainda não implementado (ver pasta Diagnostics).
        ]);
}
