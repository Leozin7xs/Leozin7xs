using WPSupportTools.Core.Logging;

namespace WPSupportTools.Core.Procedures;

/// <summary>
/// Base temporária para procedimentos cujo comando oficial da WP ainda não foi fornecido.
/// NÃO executa nenhuma ação no computador: apenas aguarda um instante (para a interface
/// mostrar o progresso) e informa que está em modo de simulação.
/// </summary>
/// <remarks>
/// Quando o comando real for definido, a classe concreta passa a implementar
/// <see cref="IProcedure"/> diretamente e deixa de herdar daqui.
/// </remarks>
public abstract class NotConfiguredProcedure : IProcedure
{
    internal static TimeSpan SimulatedDuration { get; set; } = TimeSpan.FromMilliseconds(700);

    public abstract string Id { get; }
    public abstract string DisplayName { get; }
    public abstract bool RequiresElevation { get; }

    public async Task<ProcedureResult> ExecuteAsync(ProcedureContext context, CancellationToken cancellationToken)
    {
        await Task.Delay(SimulatedDuration, cancellationToken);
        context.Log($"[SIMULAÇÃO] {Id}: comando ainda não configurado. Nenhuma ação foi executada.", LogLevel.Warning);
        return ProcedureResult.NotConfigured("Simulado: nenhuma alteração foi feita.");
    }
}
