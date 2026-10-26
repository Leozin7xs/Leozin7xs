using WPSupportTools.Core.Modules.Printing.Correction;
using WPSupportTools.Core.Modules.Printing.Labels;

namespace WPSupportTools.Core.Modules.Printing;

public static class PrintingModule
{
    public static SupportModule Definition { get; } = new(
        Id: "printing",
        DisplayName: "Impressão",
        Actions:
        [
            new SupportAction(
                Title: "Impressora A4 não está imprimindo",
                Description: "Verifica a fila de impressão, o serviço de impressão do Windows e o TSPrint.",
                Kind: SupportActionKind.Correcao,
                SequenceId: PrintingFixSequence.Id,
                IconGlyph: ""),
            new SupportAction(
                Title: "Problema na etiquetadora",
                Description: "Para a impressora de etiquetas Zebra.",
                Kind: SupportActionKind.Correcao,
                SequenceId: null,
                IconGlyph: "",
                Choices:
                [
                    new SupportAction(
                        Title: "Não sai nada",
                        Description: "Mandei imprimir a etiqueta e ela não saiu.",
                        Kind: SupportActionKind.Correcao,
                        SequenceId: LabelSequences.NotPrintingId,
                        IconGlyph: "",
                        CompletionHint: "Saiu uma etiqueta de teste. Agora tente imprimir a etiqueta pelo UniLab."),
                    new SupportAction(
                        Title: "Sai desconfigurada",
                        Description: "A etiqueta sai cortada, torta, apagada ou fora do lugar.",
                        Kind: SupportActionKind.Correcao,
                        SequenceId: LabelSequences.MisconfiguredId,
                        IconGlyph: "",
                        CompletionHint: "Confira a etiqueta de teste que acabou de sair. Depois tente imprimir pelo UniLab."),
                ]),
        ]);
}
