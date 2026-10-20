namespace WPSupportTools.Core.Procedures;

/// <summary>
/// Executa <paramref name="inner"/> somente quando <paramref name="whyNotNeeded"/> retornar null;
/// caso contrário, a etapa aparece como "não necessária" com o motivo retornado.
/// </summary>
public sealed class ConditionalProcedure(IProcedure inner, Func<RunState, string?> whyNotNeeded) : IProcedure
{
    public string Id => inner.Id;
    public string DisplayName => inner.DisplayName;
    public bool RequiresElevation => inner.RequiresElevation;
    public TimeSpan Timeout => inner.Timeout;

    public string? WhyNotNeeded(RunState state) => whyNotNeeded(state);

    public Task<ProcedureResult> ExecuteAsync(ProcedureContext context, CancellationToken cancellationToken) =>
        inner.ExecuteAsync(context, cancellationToken);
}
