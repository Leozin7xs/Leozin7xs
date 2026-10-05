using WPSupportTools.Core.Procedures;

namespace WPSupportTools.Core.Execution;

public enum SequenceOutcome
{
    /// <summary>Todas as etapas foram executadas com sucesso.</summary>
    Succeeded,

    /// <summary>A sequência terminou, mas ao menos uma etapa foi apenas simulada (sem comando real).</summary>
    Simulated,

    /// <summary>Uma etapa falhou; as seguintes não foram executadas.</summary>
    Failed,

    /// <summary>Faltou permissão de administrador (UAC negado ou processo não elevado).</summary>
    ElevationRequired,
}

public sealed record SequenceReport(
    string SequenceId,
    SequenceOutcome Outcome,
    IReadOnlyList<ProcedureResult> Results,
    int? FailedStepIndex = null,
    string? Message = null)
{
    public static SequenceReport WithoutRunning(string sequenceId, SequenceOutcome outcome, string message) =>
        new(sequenceId, outcome, [], null, message);
}
