using WPSupportTools.Core.Modules.Printing.Correction.Procedures;
using WPSupportTools.Core.Procedures;

namespace WPSupportTools.Core.Modules.Printing.Correction;

/// <summary>
/// Sequência "Corrigir problema de impressão".
/// Para mudar a ordem, adicionar ou remover etapas, altere apenas a lista abaixo.
/// </summary>
public static class PrintingFixSequence
{
    public const string Id = "printing.fix";

    public static MaintenanceSequence Create() => new(
        Id,
        "Corrigindo problema de impressão…",
        [
            new CheckPrintSpoolerProcedure(),
            new StopPrintSpoolerProcedure(),
            new ClearPrintQueueProcedure(),
            new StartPrintSpoolerProcedure(),
            new RestartPrintComponentsProcedure(),
            new FinalizeMaintenanceProcedure(),
        ]);
}
