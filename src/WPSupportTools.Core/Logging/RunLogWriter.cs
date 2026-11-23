using WPSupportTools.Core.Execution;
using WPSupportTools.Core.Procedures;

namespace WPSupportTools.Core.Logging;

/// <summary>
/// Traduz os eventos de uma execução em linhas de log legíveis. Também guarda as linhas do atendimento
/// atual (<see cref="Transcript"/>), enviadas ao painel quando ele falha.
/// </summary>
public sealed class RunLogWriter(IAppLog log, Func<DateTime>? now = null)
{
    private readonly object _gate = new();
    private System.Text.StringBuilder? _transcript;

    /// <summary>Linhas do atendimento mais recente (do início ao retorno da funcionária).</summary>
    public string Transcript
    {
        get
        {
            lock (_gate)
                return _transcript?.ToString() ?? "";
        }
    }

    private void Write(LogLevel level, string message)
    {
        log.Write(level, message);
        lock (_gate)
        {
            if (_transcript is { } t && t.Length < 200_000)
                t.Append('[').Append((now ?? (() => DateTime.Now))().ToString("HH:mm:ss"))
                    .Append("] [").Append(level).Append("] ").Append(message).Append('\n');
        }
    }

    public void ApplicationStarted() =>
        log.Write(LogLevel.Info, $"{AppInfo.Name} v{AppInfo.Version} iniciado (Windows {Environment.OSVersion.Version}).");

    public void RunStarted(MaintenanceSequence sequence, bool elevatedProcess)
    {
        lock (_gate)
            _transcript = new System.Text.StringBuilder();
        Write(LogLevel.Info,
            $"Início: {sequence.Id} — {sequence.Title} ({AppInfo.Name} v{AppInfo.Version}; " +
            $"{(elevatedProcess ? "aberto como administrador" : "administrador pedido só nas etapas que precisam")}).");
    }

    public void Handle(MaintenanceSequence sequence, RunEvent runEvent)
    {
        switch (runEvent)
        {
            case StepStartedEvent started:
                Write(LogLevel.Info, $"{StepLabel(sequence, started.Index)}: iniciando");
                break;

            case StepFinishedEvent finished:
                var r = finished.Result;
                var text = $"{StepLabel(sequence, finished.Index)}: {OutcomeLabel(r.Outcome)} — {r.Message}";
                if (!string.IsNullOrEmpty(r.ErrorDetail))
                    text += $" | Erro: {r.ErrorDetail}";
                Write(LevelFor(r.Outcome), text);
                break;

            case LogEvent entry:
                Write(entry.Level, entry.Message);
                break;
        }
    }

    public void RunFinished(MaintenanceSequence sequence, SequenceReport report)
    {
        var text = $"Fim: {sequence.Id} — resultado: {OutcomeLabel(report.Outcome)}";
        if (report.FailedStepIndex is { } failed && failed < sequence.Steps.Count)
            text += $" (etapa com falha: {sequence.Steps[failed].Id})";
        if (!string.IsNullOrEmpty(report.Message))
            text += $" — {report.Message}";

        Write(report.Outcome is SequenceOutcome.Succeeded or SequenceOutcome.Simulated
            ? LogLevel.Info : LogLevel.Error, text);
    }

    public void UserFeedback(string sequenceId, bool resolved) =>
        Write(LogLevel.Info,
            $"Retorno do usuário ({sequenceId}): {(resolved ? "funcionou" : "não funcionou — orientado a chamar o suporte")}.");

    private static string StepLabel(MaintenanceSequence sequence, int index) =>
        index >= 0 && index < sequence.Steps.Count
            ? $"Etapa {index + 1}/{sequence.Steps.Count} [{sequence.Steps[index].Id}] {sequence.Steps[index].DisplayName}"
            : $"Etapa desconhecida #{index}";

    private static LogLevel LevelFor(ProcedureOutcome outcome) => outcome switch
    {
        ProcedureOutcome.Success or ProcedureOutcome.NotNeeded => LogLevel.Info,
        ProcedureOutcome.NotConfigured or ProcedureOutcome.Skipped => LogLevel.Warning,
        _ => LogLevel.Error,
    };

    internal static string OutcomeLabel(ProcedureOutcome outcome) => outcome switch
    {
        ProcedureOutcome.Success => "OK",
        ProcedureOutcome.NotConfigured => "SIMULADO",
        ProcedureOutcome.NotNeeded => "NÃO NECESSÁRIO",
        ProcedureOutcome.Skipped => "NÃO EXECUTADO",
        ProcedureOutcome.Failure => "FALHA",
        ProcedureOutcome.ElevationRequired => "SEM PERMISSÃO DE ADMINISTRADOR",
        _ => outcome.ToString(),
    };

    internal static string OutcomeLabel(SequenceOutcome outcome) => outcome switch
    {
        SequenceOutcome.Succeeded => "concluída com sucesso",
        SequenceOutcome.Simulated => "concluída em modo de simulação",
        SequenceOutcome.Failed => "falhou",
        SequenceOutcome.ElevationRequired => "sem permissão de administrador",
        _ => outcome.ToString(),
    };
}
