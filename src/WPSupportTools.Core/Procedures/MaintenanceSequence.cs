namespace WPSupportTools.Core.Procedures;

/// <summary>Uma lista ordenada e fixa de procedimentos, executada sempre da mesma forma.</summary>
public sealed class MaintenanceSequence
{
    public MaintenanceSequence(string id, string title, IReadOnlyList<IProcedure> steps)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        if (steps.Count == 0)
            throw new ArgumentException("A sequência precisa de ao menos um procedimento.", nameof(steps));

        var duplicate = steps.GroupBy(s => s.Id).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new ArgumentException($"Procedimento repetido na sequência: {duplicate.Key}", nameof(steps));

        Id = id;
        Title = title;
        Steps = steps;
    }

    public string Id { get; }

    /// <summary>Título exibido durante a execução. Ex.: "Corrigindo problema de impressão…".</summary>
    public string Title { get; }

    public IReadOnlyList<IProcedure> Steps { get; }

    public bool RequiresElevation => Steps.Any(s => s.RequiresElevation);
}
