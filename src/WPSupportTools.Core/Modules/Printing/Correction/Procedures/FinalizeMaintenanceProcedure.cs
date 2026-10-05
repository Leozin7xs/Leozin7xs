using WPSupportTools.Core.Procedures;

namespace WPSupportTools.Core.Modules.Printing.Correction.Procedures;

/// <summary>
/// Confirma o estado final após a manutenção.
/// </summary>
/// <remarks>
/// STATUS: aguardando o comando oficial utilizado no ambiente da WP.
/// Enquanto isso, esta etapa roda em modo de simulação e não altera nada no computador.
/// </remarks>
public sealed class FinalizeMaintenanceProcedure : NotConfiguredProcedure
{
    public override string Id => "printing.finalize";
    public override string DisplayName => "Finalizando manutenção";
    public override bool RequiresElevation => false;
}
