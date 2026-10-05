using WPSupportTools.Core.Procedures;

namespace WPSupportTools.Core.Modules.Printing.Correction.Procedures;

/// <summary>
/// Para o serviço de impressão.
/// </summary>
/// <remarks>
/// STATUS: aguardando o comando oficial utilizado no ambiente da WP.
/// Enquanto isso, esta etapa roda em modo de simulação e não altera nada no computador.
/// </remarks>
public sealed class StopPrintSpoolerProcedure : NotConfiguredProcedure
{
    public override string Id => "printing.stop-spooler";
    public override string DisplayName => "Parando serviço de impressão";
    public override bool RequiresElevation => true;
}
