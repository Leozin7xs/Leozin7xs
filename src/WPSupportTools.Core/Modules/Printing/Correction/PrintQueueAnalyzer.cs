using WPSupportTools.Core.Platform;

namespace WPSupportTools.Core.Modules.Printing.Correction;

public sealed record QueueAssessment(bool IsStuck, int TotalJobs, int JobsWithProblem, string Summary);

/// <summary>
/// Regra para "fila travada": existe ao menos um documento com status de
/// erro, offline, sem papel, bloqueado ou pedindo intervenção.
/// Documento apenas demorando (sem erro) NÃO conta: isso é tratado à parte pelo suporte.
/// </summary>
public static class PrintQueueAnalyzer
{
    private const PrintJobStatus ProblemFlags =
        PrintJobStatus.Error | PrintJobStatus.Offline | PrintJobStatus.PaperOut |
        PrintJobStatus.Blocked | PrintJobStatus.UserInterventionRequired;

    public static bool HasProblem(PrintJobInfo job) => (job.Status & ProblemFlags) != 0;

    /// <summary>Impressoras com ao menos um documento travado, em ordem alfabética.</summary>
    public static IReadOnlyList<string> PrintersWithStuckJobs(IReadOnlyList<PrintJobInfo> jobs) =>
        jobs.Where(HasProblem).Select(j => j.PrinterName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static QueueAssessment Assess(IReadOnlyList<PrintJobInfo> jobs)
    {
        if (jobs.Count == 0)
            return new QueueAssessment(false, 0, 0, "Fila vazia.");

        var withProblem = jobs.Count(HasProblem);

        var perPrinter = string.Join("; ", jobs
            .GroupBy(j => j.PrinterName)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => $"{g.Key}: {g.Count()} ({string.Join(", ", g.Select(j => j.Status).Distinct())})"));

        var summary = $"{jobs.Count} documento(s) na fila ({perPrinter}); com erro: {withProblem}.";
        return new QueueAssessment(withProblem > 0, jobs.Count, withProblem, summary);
    }
}
