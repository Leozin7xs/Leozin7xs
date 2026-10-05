using WPSupportTools.Core.Procedures;

namespace WPSupportTools.Core.Modules.Printing.Correction.Procedures;

/// <summary>
/// Inicia novamente o serviço de impressão.
/// </summary>
/// <remarks>
/// STATUS: aguardando o comando oficial utilizado no ambiente da WP.
/// Enquanto isso, esta etapa roda em modo de simulação e não altera nada no computador.
/// </remarks>
public sealed class StartPrintSpoolerProcedure : NotConfiguredProcedure
{
    public override string Id => "printing.start-spooler";
    public override string DisplayName => "Iniciando serviço de impressão";
    public override bool RequiresElevation => true;
}
