using WPSupportTools.Core.Modules.Printing.Correction.Procedures;
using WPSupportTools.Core.Platform;
using WPSupportTools.Core.Procedures;

namespace WPSupportTools.Core.Modules.Printing.Correction;

/// <summary>
/// Sequência "Corrigir problema de impressão", conforme o procedimento manual da WP:
/// <code>
/// 1. Ver se a fila de impressão está travada (documento com erro).
///    Se SIM: cancelar os documentos da fila travada, parar o spooler (net stop spooler), apagar os
///            arquivos da fila, iniciar o spooler (net start spooler), cancelar o que voltar e
///            conferir se a fila ficou vazia.
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

            // Fila travada (etapas 2 a 6: um único pedido de administrador)
            new ConditionalProcedure(new CancelStuckJobsProcedure(platform), OnlyIfQueueStuck),
            new ConditionalProcedure(new StopPrintSpoolerProcedure(platform), OnlyIfQueueStuck),
            new ConditionalProcedure(new ClearSpoolFilesProcedure(platform), OnlyIfQueueStuck),
            new ConditionalProcedure(new StartPrintSpoolerProcedure(platform), OnlyIfQueueStuck),
            new ConditionalProcedure(new CancelStuckJobsProcedure(platform, afterRestart: true), OnlyIfQueueStuck),
            new ConditionalProcedure(new RecheckPrintQueueProcedure(platform), OnlyIfQueueStuck),

            // Fila normal
            new ConditionalProcedure(new CheckTSPrintProcedure(platform), OnlyIfQueueNormal),
            new ConditionalProcedure(new StartTSPrintProcedure(platform),
                s => OnlyIfQueueNormal(s) ?? (s.IsSet(PrintingState.TSPrintRunning)
                    ? "Não necessário: o TSPrint já está aberto."
                    : null)),
        ],
        transferableStateKeys: [PrintingState.TargetPrinters]);

    internal static string? OnlyIfQueueStuck(RunState state) =>
        state.IsSet(PrintingState.QueueNeedsReset) ? null : "Não necessário: a fila não está travada.";

    internal static string? OnlyIfQueueNormal(RunState state) =>
        state.IsSet(PrintingState.QueueNeedsReset) ? "Não necessário: a fila foi reiniciada." : null;
}
