using WPSupportTools.Core.Procedures;

namespace WPSupportTools.Core.Execution;

/// <summary>
/// Executa uma sequência de forma determinística: etapas na ordem definida, uma de cada vez.
/// <list type="bullet">
///   <item>Etapas cuja condição não se aplica aparecem como "não necessárias".</item>
///   <item>Na primeira falha a execução para; as etapas restantes não são executadas,
///         exceto as marcadas com <see cref="IProcedure.RunsEvenAfterFailure"/> (ex.: religar o spooler).</item>
///   <item>Etapas de administrador são enviadas, em bloco, ao <see cref="IElevatedStepRunner"/>
///         (uma única confirmação do UAC por bloco). As demais rodam no próprio processo.</item>
///   <item>Não há novas tentativas nem ações alternativas.</item>
/// </list>
/// </summary>
public sealed class SequenceRunner
{
    private readonly bool _isElevated;
    private readonly IElevatedStepRunner? _elevatedRunner;

    public SequenceRunner(IElevatedStepRunner? elevatedRunner = null)
        : this(Environment.IsPrivilegedProcess, elevatedRunner)
    {
    }

    internal SequenceRunner(bool isElevated, IElevatedStepRunner? elevatedRunner = null)
    {
        _isElevated = isElevated;
        _elevatedRunner = elevatedRunner;
    }

    public async Task<SequenceReport> RunAsync(
        MaintenanceSequence sequence, IRunEventSink sink, CancellationToken cancellationToken = default)
    {
        var context = new ProcedureContext(sink, _isElevated);
        var results = new ProcedureResult[sequence.Steps.Count];
        int? failedIndex = null;

        void Finish(int index, ProcedureResult result)
        {
            results[index] = result;
            sink.Publish(new StepFinishedEvent(index, sequence.Steps[index].Id, result));
            if (!result.IsSuccessful)
                failedIndex ??= index;
        }

        for (var i = 0; i < sequence.Steps.Count;)
        {
            var step = sequence.Steps[i];

            if (failedIndex is not null && !step.RunsEvenAfterFailure)
            {
                Finish(i++, ProcedureResult.Skipped("Não executado porque uma etapa anterior falhou."));
                continue;
            }

            if (step.WhyNotNeeded(context.State) is { } reason)
            {
                Finish(i++, ProcedureResult.NotNeeded(reason));
                continue;
            }

            if (step.RequiresElevation && !_isElevated && _elevatedRunner is not null)
            {
                var block = ElevatedBlockFrom(sequence, i, context.State, afterFailure: failedIndex is not null);
                var state = context.State.TextsFor(sequence.TransferableStateKeys);
                var blockResults = await _elevatedRunner.RunAsync(sequence, block, state, sink, cancellationToken);
                for (var k = 0; k < block.Count; k++)
                {
                    var result = k < blockResults.Count
                        ? blockResults[k]
                        : ProcedureResult.Fail("O processo de administrador não informou o resultado.");
                    results[block[k]] = result;
                    if (!result.IsSuccessful)
                        failedIndex ??= block[k];
                }

                i = block[^1] + 1;
                continue;
            }

            sink.Publish(new StepStartedEvent(i, step.Id));
            Finish(i, await ExecuteStepAsync(step, context, cancellationToken));
            i++;
        }

        return new SequenceReport(sequence.Id, Summarize(results, failedIndex), results, failedIndex);
    }

    /// <summary>
    /// Usado pelo processo elevado: executa somente os índices recebidos, na ordem.
    /// Na primeira falha, os seguintes são marcados como não executados.
    /// </summary>
    public async Task<IReadOnlyList<ProcedureResult>> RunStepsAsync(
        MaintenanceSequence sequence, IReadOnlyList<int> stepIndices, IRunEventSink sink,
        CancellationToken cancellationToken = default, IReadOnlyDictionary<string, string>? state = null)
    {
        var context = new ProcedureContext(sink, _isElevated);
        if (state is not null)
            context.State.SetTexts(state);
        var results = new List<ProcedureResult>(stepIndices.Count);
        var failed = false;

        foreach (var index in stepIndices)
        {
            var step = sequence.Steps[index];
            ProcedureResult result;

            if (failed && !step.RunsEvenAfterFailure)
            {
                result = ProcedureResult.Skipped("Não executado porque uma etapa anterior falhou.");
            }
            else
            {
                sink.Publish(new StepStartedEvent(index, step.Id));
                result = await ExecuteStepAsync(step, context, cancellationToken);
                failed |= !result.IsSuccessful;
            }

            results.Add(result);
            sink.Publish(new StepFinishedEvent(index, step.Id, result));
        }

        return results;
    }

    /// <summary>Etapas consecutivas, a partir de <paramref name="start"/>, que exigem administrador e são necessárias.</summary>
    private static List<int> ElevatedBlockFrom(MaintenanceSequence sequence, int start, RunState state, bool afterFailure)
    {
        var block = new List<int> { start };
        for (var j = start + 1; j < sequence.Steps.Count; j++)
        {
            var next = sequence.Steps[j];
            if (!next.RequiresElevation || next.WhyNotNeeded(state) is not null ||
                (afterFailure && !next.RunsEvenAfterFailure))
                break;
            block.Add(j);
        }

        return block;
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

    private static SequenceOutcome Summarize(ProcedureResult[] results, int? failedIndex)
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
