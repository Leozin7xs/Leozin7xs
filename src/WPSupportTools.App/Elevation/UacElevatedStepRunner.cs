using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using WPSupportTools.Core.Execution;
using WPSupportTools.Core.Procedures;

namespace WPSupportTools.App.Elevation;

/// <summary>
/// Executa um bloco de etapas em um processo administrador, usando o mecanismo oficial do Windows (UAC):
/// <list type="number">
///   <item>Cria um canal local (named pipe) com nome aleatório.</item>
///   <item>Inicia este mesmo executável com o verbo "runas" — o Windows exibe o aviso do UAC.</item>
///   <item>O processo elevado recebe apenas o ID da sequência e os números das etapas
///         (validados contra a lista fechada do programa).</item>
///   <item>A interface confere que quem se conectou ao canal é exatamente o processo que ela iniciou.</item>
///   <item>O progresso volta pelo canal e é exibido/registrado pela interface.</item>
/// </list>
/// </summary>
internal sealed class UacElevatedStepRunner : IElevatedStepRunner
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(30);

    public async Task<IReadOnlyList<ProcedureResult>> RunAsync(
        MaintenanceSequence sequence, IReadOnlyList<int> stepIndices, IRunEventSink sink,
        CancellationToken cancellationToken)
    {
        // Resultados que o processo elevado já informou, para completar o restante em caso de falha.
        var reported = new Dictionary<int, ProcedureResult>();
        var forwarding = new DelegateRunEventSink(e =>
        {
            if (e is StepFinishedEvent finished)
                reported[finished.Index] = finished.Result;
            sink.Publish(e);
        });

        var (results, failure) = await RunWorkerAsync(sequence, stepIndices, forwarding, cancellationToken);
        if (results is not null && results.Count == stepIndices.Count)
            return results;

        failure ??= ProcedureResult.Fail("O processo de manutenção não informou o resultado.");
        var completed = new List<ProcedureResult>(stepIndices.Count);
        var failureUsed = false;
        foreach (var index in stepIndices)
        {
            if (reported.TryGetValue(index, out var known))
            {
                completed.Add(known);
                continue;
            }

            var result = failureUsed
                ? ProcedureResult.Skipped("Não executado.")
                : failure;
            failureUsed = true;
            completed.Add(result);
            sink.Publish(new StepFinishedEvent(index, sequence.Steps[index].Id, result));
        }

        return completed;
    }

    private static async Task<(IReadOnlyList<ProcedureResult>? Results, ProcedureResult? Failure)> RunWorkerAsync(
        MaintenanceSequence sequence, IReadOnlyList<int> stepIndices, IRunEventSink sink,
        CancellationToken cancellationToken)
    {
        var args = new WorkerArguments(sequence.Id, stepIndices, WorkerArguments.NewPipeName(), Environment.ProcessId);

        await using var server = new NamedPipeServerStream(
            args.PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

        Process? worker;
        try
        {
            // Process.Start bloqueia enquanto o aviso do UAC está aberto; por isso fora da thread da interface.
            worker = await Task.Run(() => Process.Start(new ProcessStartInfo
            {
                FileName = Environment.ProcessPath!,
                Arguments = args.ToCommandLine(),
                UseShellExecute = true,
                Verb = "runas",
            }), cancellationToken);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == NativeMethods.ErrorCancelled)
        {
            return (null, ProcedureResult.NeedsElevation(
                "A permissão de administrador não foi concedida. Nada foi alterado."));
        }
        catch (Win32Exception ex)
        {
            return (null, ProcedureResult.Fail("Não foi possível iniciar o processo de manutenção.", ex.Message));
        }

        if (worker is null)
            return (null, ProcedureResult.Fail("Não foi possível iniciar o processo de manutenção."));

        using (worker)
        {
            if (!await WaitForWorkerConnectionAsync(server, worker, cancellationToken))
                return (null, ProcedureResult.Fail("O processo de manutenção não respondeu."));

            if (!NativeMethods.GetNamedPipeClientProcessId(server.SafePipeHandle, out var clientPid) ||
                clientPid != worker.Id)
                return (null, ProcedureResult.Fail("Conexão recusada: o processo conectado não é o esperado."));

            return await ReadEventsAsync(server, sequence, stepIndices, sink, cancellationToken);
        }
    }

    private static async Task<bool> WaitForWorkerConnectionAsync(
        NamedPipeServerStream server, Process worker, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(ConnectTimeout);

        var connect = server.WaitForConnectionAsync(cts.Token);
        var exited = worker.WaitForExitAsync(cts.Token);

        try
        {
            await Task.WhenAny(connect, exited);
        }
        finally
        {
            cts.Cancel();
        }

        return connect.IsCompletedSuccessfully;
    }

    private static async Task<(IReadOnlyList<ProcedureResult>? Results, ProcedureResult? Failure)> ReadEventsAsync(
        NamedPipeServerStream server, MaintenanceSequence sequence, IReadOnlyList<int> stepIndices,
        IRunEventSink sink, CancellationToken cancellationToken)
    {
        // Limite geral: soma dos limites de cada etapa do bloco + margem.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(stepIndices.Aggregate(TimeSpan.FromMinutes(1), (total, i) => total + sequence.Steps[i].Timeout));

        using var reader = new StreamReader(server, new UTF8Encoding(false));
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
            return (null, ProcedureResult.Fail("O processo de manutenção excedeu o tempo limite."));
        }
        catch (IOException)
        {
            // Canal fechado inesperadamente; tratado abaixo.
        }

        return (null, ProcedureResult.Fail("O processo de manutenção foi encerrado antes de concluir."));
    }
}
