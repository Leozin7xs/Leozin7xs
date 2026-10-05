using WPSupportTools.Core.Procedures;

namespace WPSupportTools.Core.Modules.Printing.Correction.Procedures;

/// <summary>
/// Remove os trabalhos pendentes da fila de impressão, somente na pasta autorizada.
/// </summary>
/// <remarks>
/// STATUS: aguardando o comando oficial utilizado no ambiente da WP.
/// Enquanto isso, esta etapa roda em modo de simulação e não altera nada no computador.
/// </remarks>
public sealed class ClearPrintQueueProcedure : NotConfiguredProcedure
{
    public override string Id => "printing.clear-queue";
    public override string DisplayName => "Limpando fila de impressão";
    public override bool RequiresElevation => true;
}
