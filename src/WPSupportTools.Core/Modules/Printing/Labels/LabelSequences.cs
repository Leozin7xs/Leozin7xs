using WPSupportTools.Core.Modules.Printing.Correction;
using WPSupportTools.Core.Modules.Printing.Correction.Procedures;
using WPSupportTools.Core.Modules.Printing.Labels.Procedures;
using WPSupportTools.Core.Platform;
using WPSupportTools.Core.Procedures;

namespace WPSupportTools.Core.Modules.Printing.Labels;

/// <summary>Sequências do botão "Problema na etiquetadora".</summary>
public static class LabelSequences
{
    public const string NotPrintingId = "labels.not-printing";
    public const string MisconfiguredId = "labels.misconfigured";

    public static MaintenanceSequence CreateNotPrinting() => CreateNotPrinting(PlatformServices.Windows);

    public static MaintenanceSequence CreateMisconfigured() => CreateMisconfigured(PlatformServices.Windows);

    /// <summary>
    /// "Não sai nada": localizar a Zebra (avisa se estiver desligada), ver a fila dela;
    /// se travada → cancelar, reiniciar spooler, conferir; se normal → TSPrint.
    /// No fim, etiqueta de teste: se ela sai mas a do UniLab não, o problema está no TSPrint/UniLab.
    /// </summary>
    internal static MaintenanceSequence CreateNotPrinting(PlatformServices platform) => new(
        NotPrintingId,
        "Verificando a etiquetadora…",
        [
            new LocateLabelPrinterProcedure(platform),
            new CheckPrintQueueProcedure(platform, LabelPrinter),
            new ConditionalProcedure(new CancelStuckJobsProcedure(platform), PrintingFixSequence.OnlyIfQueueStuck),
            new ConditionalProcedure(new StopPrintSpoolerProcedure(platform), PrintingFixSequence.OnlyIfQueueStuck),
            new ConditionalProcedure(new StartPrintSpoolerProcedure(platform), PrintingFixSequence.OnlyIfQueueStuck),
            new ConditionalProcedure(new RecheckPrintQueueProcedure(platform, LabelPrinter), PrintingFixSequence.OnlyIfQueueStuck),
            new ConditionalProcedure(new CheckTSPrintProcedure(platform), PrintingFixSequence.OnlyIfQueueNormal),
            new ConditionalProcedure(new StartTSPrintProcedure(platform),
                s => PrintingFixSequence.OnlyIfQueueNormal(s) ?? (s.IsSet(PrintingState.TSPrintRunning)
                    ? "Não necessário: o TSPrint já está aberto."
                    : null)),
            new PrintTestLabelProcedure(platform),
        ]);

    /// <summary>
    /// "Sai desconfigurada": localizar a Zebra, conferir/corrigir tamanho e orientação,
    /// calibrar e imprimir etiqueta de teste.
    /// </summary>
    internal static MaintenanceSequence CreateMisconfigured(PlatformServices platform) => new(
        MisconfiguredId,
        "Ajustando a etiquetadora…",
        [
            new LocateLabelPrinterProcedure(platform),
            new CheckLabelSizeProcedure(platform),
            new ConditionalProcedure(new FixLabelSizeProcedure(platform),
                s => s.IsSet(LabelState.SizeNeedsFix) ? null : "Não necessário: o tamanho já está no padrão."),
            new CalibrateLabelPrinterProcedure(platform),
            new PrintTestLabelProcedure(platform),
        ]);

    private static string? LabelPrinter(RunState state) => state.GetText(LabelState.PrinterName);
}
