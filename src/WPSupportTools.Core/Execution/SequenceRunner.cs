using WPSupportTools.Core.Procedures;

namespace WPSupportTools.Core.Execution;

/// <summary>
/// Executa uma sequência de forma determinística: etapas na ordem definida, uma de cada vez.
/// Na primeira falha a execução para e as etapas restantes são marcadas como não executadas.
/// Não há novas tentativas nem ações alternativas.
/// </summary>
public sealed class SequenceRunner
{
    private readonly bool _isElevated;

    public SequenceRunner() : this(Environment.IsPrivilegedProcess)
    {
    }

    internal SequenceRunner(bool isElevated) => _isElevated = isElevated;

    public async Task<SequenceReport> RunAsync(
        MaintenanceSequence sequence, IRunEventSink sink, CancellationToken cancellationToken = default)
    {
        var context = new ProcedureContext(sink, _isElevated);
        var results = new List<ProcedureResult>(sequence.Steps.Count);
        int? failedIndex = null;

        for (var i = 0; i < sequence.Steps.Count; i++)
        {
            var step = sequence.Steps[i];

            if (failedIndex is not null)
            {
                var skipped = ProcedureResult.Skipped("Não executado porque uma etapa anterior falhou.");
                results.Add(skipped);
                sink.Publish(new StepFinishedEvent(i, step.Id, skipped));
                continue;
            }

            sink.Publish(new StepStartedEvent(i, step.Id));
            var result = await ExecuteStepAsync(step, context, cancellationToken);
            results.Add(result);
            sink.Publish(new StepFinishedEvent(i, step.Id, result));

            if (!result.IsSuccessful)
                failedIndex = i;
        }

        return new SequenceReport(sequence.Id, Summarize(results, failedIndex), results, failedIndex);
    }

    private async Task<ProcedureResult> ExecuteStepAsync(
        IProcedure step, ProcedureContext context, CancellationToken cancellationToken)
    {
        if (step.RequiresElevation && !_isElevated)
            return ProcedureResult.NeedsElevation("Esta etapa precisa de permissão de administrador.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(step.Timeout);

        try
        {
            return await step.ExecuteAsync(context, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ProcedureResult.Fail($"Tempo limite excedido ({step.Timeout.TotalSeconds:0} s).");
        }
        catch (UnauthorizedAccessException ex)
        {
            return new ProcedureResult(ProcedureOutcome.ElevationRequired,
                "Acesso negado. É necessária permissão de administrador.", Describe(ex));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ProcedureResult.Fail("Erro inesperado ao executar a etapa.", Describe(ex));
        }
    }

    private static SequenceOutcome Summarize(List<ProcedureResult> results, int? failedIndex)
    {
        if (failedIndex is { } index)
        {
            return results[index].Outcome == ProcedureOutcome.ElevationRequired
                ? SequenceOutcome.ElevationRequired
                : SequenceOutcome.Failed;
        }

        return results.Any(r => r.Outcome == ProcedureOutcome.NotConfigured)
            ? SequenceOutcome.Simulated
            : SequenceOutcome.Succeeded;
    }

    private static string Describe(Exception ex) => $"{ex.GetType().Name}: {ex.Message}";
}
