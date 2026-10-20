using WPSupportTools.Core.Modules.Printing.Correction.Procedures;
using WPSupportTools.Core.Platform;
using WPSupportTools.Core.Procedures;

namespace WPSupportTools.Core.Modules.Printing.Correction;

/// <summary>
/// Sequência "Corrigir problema de impressão", conforme o procedimento manual da WP:
/// <code>
/// 1. Ver se a fila de impressão está travada.
///    Se SIM: parar o spooler, limpar a fila, iniciar o spooler   (net stop / net start spooler)
///    Se NÃO: ver se o TSPrint está aberto; se não estiver, abrir.
/// </code>
/// Para mudar a ordem, adicionar ou remover etapas, altere apenas a lista abaixo.
/// </summary>
public static class PrintingFixSequence
{
    public const string Id = "printing.fix";

    public static MaintenanceSequence Create() => Create(PlatformServices.Windows);

    internal static MaintenanceSequence Create(PlatformServices platform) => new(
        Id,
        "Corrigindo problema de impressão…",
        [
            new CheckPrintQueueProcedure(platform),

            // Fila travada
            new ConditionalProcedure(new StopPrintSpoolerProcedure(platform), OnlyIfQueueStuck),
            new ConditionalProcedure(new ClearPrintQueueProcedure(platform), OnlyIfQueueStuck),
            new ConditionalProcedure(new StartPrintSpoolerProcedure(platform), OnlyIfQueueStuck),

            // Fila normal
            new ConditionalProcedure(new CheckTSPrintProcedure(platform), OnlyIfQueueNormal),
            new ConditionalProcedure(new StartTSPrintProcedure(platform),
                s => OnlyIfQueueNormal(s) ?? (s.IsSet(PrintingState.TSPrintRunning)
                    ? "Não necessário: o TSPrint já está aberto."
                    : null)),
        ]);

    private static string? OnlyIfQueueStuck(RunState state) =>
        state.IsSet(PrintingState.QueueNeedsReset) ? null : "Não necessário: a fila não está travada.";

    private static string? OnlyIfQueueNormal(RunState state) =>
        state.IsSet(PrintingState.QueueNeedsReset) ? "Não necessário: a fila foi reiniciada." : null;
}
