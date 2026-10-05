using WPSupportTools.Core.Procedures;

namespace WPSupportTools.Core.Modules.Printing.Correction.Procedures;

/// <summary>
/// Verifica se o serviço de impressão existe e qual é o seu estado. Somente leitura.
/// </summary>
/// <remarks>
/// STATUS: aguardando o comando oficial utilizado no ambiente da WP.
/// Enquanto isso, esta etapa roda em modo de simulação e não altera nada no computador.
/// </remarks>
public sealed class CheckPrintSpoolerProcedure : NotConfiguredProcedure
{
    public override string Id => "printing.check-spooler";
    public override string DisplayName => "Verificando serviço de impressão";
    public override bool RequiresElevation => false;
}
