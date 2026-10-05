using WPSupportTools.Core.Procedures;

namespace WPSupportTools.Core.Modules.Printing.Correction.Procedures;

/// <summary>
/// Reinicia os demais componentes de impressão usados na WP (a definir).
/// </summary>
/// <remarks>
/// STATUS: aguardando o comando oficial utilizado no ambiente da WP.
/// Enquanto isso, esta etapa roda em modo de simulação e não altera nada no computador.
/// </remarks>
public sealed class RestartPrintComponentsProcedure : NotConfiguredProcedure
{
    public override string Id => "printing.restart-components";
    public override string DisplayName => "Reiniciando componentes necessários";
    public override bool RequiresElevation => true;
}
