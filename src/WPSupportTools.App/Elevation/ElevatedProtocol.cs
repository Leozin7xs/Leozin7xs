using System.IO;
using System.Text;
using WPSupportTools.Core.Execution;
using WPSupportTools.Core.Procedures;

namespace WPSupportTools.App.Elevation;

/// <summary>Leitura dos eventos que vêm da parte de administrador (processo elevado ou serviço).</summary>
internal static class ElevatedProtocol
{
    /// <summary>
    /// Executa <paramref name="run"/> repassando os eventos e garante um resultado por etapa:
    /// as que a parte de administrador não informou recebem a falha (a primeira) ou "não executado".
    /// </summary>
    public static async Task<IReadOnlyList<ProcedureResult>> RunAndCompleteAsync(
        MaintenanceSequence sequence, IReadOnlyList<int> stepIndices, IRunEventSink sink,
        Func<IRunEventSink, Task<(IReadOnlyList<ProcedureResult>? Results, ProcedureResult? Failure)>> run)
    {
        var reported = new Dictionary<int, ProcedureResult>();
        var forwarding = new DelegateRunEventSink(e =>
        {
            if (e is StepFinishedEvent finished)
                reported[finished.Index] = finished.Result;
            sink.Publish(e);
        });

        var (results, failure) = await run(forwarding);
        if (results is not null && results.Count == stepIndices.Count)
            return results;

        failure ??= ProcedureResult.Fail("A parte de administrador não informou o resultado.");
        var completed = new List<ProcedureResult>(stepIndices.Count);
        var failureUsed = false;
        foreach (var index in stepIndices)
        {
            if (reported.TryGetValue(index, out var known))
            {
                completed.Add(known);
                continue;
            }

            var result = failureUsed ? ProcedureResult.Skipped("Não executado.") : failure;
            failureUsed = true;
            completed.Add(result);
            sink.Publish(new StepFinishedEvent(index, sequence.Steps[index].Id, result));
        }

        return completed;
    }

    /// <summary>Lê eventos (uma linha JSON cada) até o resultado final do bloco.</summary>
    public static async Task<(IReadOnlyList<ProcedureResult>? Results, ProcedureResult? Failure)> ReadEventsAsync(
        Stream stream, MaintenanceSequence sequence, IReadOnlyList<int> stepIndices, IRunEventSink sink,
        CancellationToken cancellationToken)
    {
        // Limite geral: soma dos limites de cada etapa do bloco + margem.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(stepIndices.Aggregate(TimeSpan.FromMinutes(1), (total, i) => total + sequence.Steps[i].Timeout));

        using var reader = new StreamReader(stream, new UTF8Encoding(false), leaveOpen: true);
        try
        {
            while (await reader.ReadLineAsync(cts.Token) is { } line)
            {
                var runEvent = RunEventSerializer.TryDeserialize(line);
                if (runEvent is StepsCompletedEvent done && done.Indices.SequenceEqual(stepIndices))
                    return (done.Results, null);
                if (runEvent is StepStartedEvent or StepFinishedEvent or LogEvent)
                    sink.Publish(runEvent);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (null, ProcedureResult.Fail("A parte de administrador excedeu o tempo limite."));
        }
        catch (IOException)
        {
            // Canal fechado inesperadamente; tratado abaixo.
        }

        return (null, ProcedureResult.Fail("A parte de administrador foi encerrada antes de concluir."));
    }
}
