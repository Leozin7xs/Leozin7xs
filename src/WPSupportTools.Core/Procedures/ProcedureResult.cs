namespace WPSupportTools.Core.Procedures;

public enum ProcedureOutcome
{
    /// <summary>O procedimento foi executado e concluiu com sucesso.</summary>
    Success,

    /// <summary>
    /// O procedimento ainda não tem comando real definido. Nada foi alterado no computador.
    /// Usado apenas enquanto os comandos oficiais da WP não forem fornecidos.
    /// </summary>
    NotConfigured,

    /// <summary>A etapa não se aplica a esta situação (ex.: "parar o spooler" quando a fila não está travada).</summary>
    NotNeeded,

    /// <summary>O procedimento não foi executado porque uma etapa anterior falhou.</summary>
    Skipped,

    /// <summary>O procedimento falhou.</summary>
    Failure,

    /// <summary>O procedimento exige privilégios de administrador que o processo não possui.</summary>
    ElevationRequired,
}

public sealed record ProcedureResult(ProcedureOutcome Outcome, string Message, string? ErrorDetail = null)
{
    public bool IsSuccessful => Outcome is ProcedureOutcome.Success or ProcedureOutcome.NotConfigured or ProcedureOutcome.NotNeeded;

    public static ProcedureResult Ok(string message) => new(ProcedureOutcome.Success, message);

    public static ProcedureResult Fail(string message, string? errorDetail = null) =>
        new(ProcedureOutcome.Failure, message, errorDetail);

    public static ProcedureResult NeedsElevation(string message) =>
        new(ProcedureOutcome.ElevationRequired, message);

    public static ProcedureResult NotConfigured(string message) =>
        new(ProcedureOutcome.NotConfigured, message);

    public static ProcedureResult NotNeeded(string message) =>
        new(ProcedureOutcome.NotNeeded, message);

    public static ProcedureResult Skipped(string message) =>
        new(ProcedureOutcome.Skipped, message);
}
