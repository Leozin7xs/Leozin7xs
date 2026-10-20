using WPSupportTools.Core.Modules.Printing;
using WPSupportTools.Core.Platform;

namespace WPSupportTools.Core.Modules.Printing.Correction;

public sealed record QueueAssessment(bool IsStuck, int TotalJobs, int JobsWithProblem, int OldJobs, string Summary);

/// <summary>
/// Regra para "fila travada": existe ao menos um trabalho
/// (a) com erro, offline, sem papel, bloqueado ou pedindo intervenção; ou
/// (b) parado na fila há mais de <see cref="PrintingConstants.StuckJobAge"/>.
/// </summary>
public static class PrintQueueAnalyzer
{
    private const PrintJobStatus ProblemFlags =
        PrintJobStatus.Error | PrintJobStatus.Offline | PrintJobStatus.PaperOut |
        PrintJobStatus.Blocked | PrintJobStatus.UserInterventionRequired;

    public static QueueAssessment Assess(IReadOnlyList<PrintJobInfo> jobs, DateTime now)
    {
        if (jobs.Count == 0)
            return new QueueAssessment(false, 0, 0, 0, "Fila vazia.");

        var withProblem = jobs.Count(j => (j.Status & ProblemFlags) != 0);
        var old = jobs.Count(j => j.SubmittedAt is { } at && now - at > PrintingConstants.StuckJobAge);

        var perPrinter = string.Join("; ", jobs
            .GroupBy(j => j.PrinterName)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => $"{g.Key}: {g.Count()}"));

        var summary = $"{jobs.Count} trabalho(s) na fila ({perPrinter}); com erro: {withProblem}; " +
                      $"há mais de {PrintingConstants.StuckJobAge.TotalMinutes:0} min: {old}.";

        return new QueueAssessment(withProblem > 0 || old > 0, jobs.Count, withProblem, old, summary);
    }
}
