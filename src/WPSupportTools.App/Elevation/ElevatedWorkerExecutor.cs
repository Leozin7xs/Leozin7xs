using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using WPSupportTools.Core.Execution;
using WPSupportTools.Core.Procedures;

namespace WPSupportTools.App.Elevation;

/// <summary>
/// Executa uma sequência em um processo administrador, usando o mecanismo oficial do Windows (UAC):
/// <list type="number">
///   <item>Cria um canal local (named pipe) com nome aleatório.</item>
///   <item>Inicia este mesmo executável com o verbo "runas" — o Windows exibe o aviso do UAC.</item>
///   <item>O processo elevado recebe apenas o ID da sequência (lista fechada no programa).</item>
///   <item>A interface confere que quem se conectou ao canal é exatamente o processo que ela iniciou.</item>
///   <item>O progresso volta pelo canal e é exibido/registrado pela interface.</item>
/// </list>
/// </summary>
internal sealed class ElevatedWorkerExecutor : ISequenceExecutor
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(30);

    public async Task<SequenceReport> RunAsync(
        MaintenanceSequence sequence, IRunEventSink sink, CancellationToken cancellationToken)
    {
        var args = new WorkerArguments(sequence.Id, WorkerArguments.NewPipeName(), Environment.ProcessId);

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
            return SequenceReport.WithoutRunning(sequence.Id, SequenceOutcome.ElevationRequired,
                "A permissão de administrador não foi concedida. Nenhuma alteração foi feita.");
        }
        catch (Win32Exception ex)
        {
            return Failed(sequence, $"Não foi possível iniciar o processo de manutenção ({ex.Message}).");
        }

        if (worker is null)
            return Failed(sequence, "Não foi possível iniciar o processo de manutenção.");

        using (worker)
        {
            if (!await WaitForWorkerConnectionAsync(server, worker, cancellationToken))
                return Failed(sequence, "O processo de manutenção não respondeu.");

            if (!NativeMethods.GetNamedPipeClientProcessId(server.SafePipeHandle, out var clientPid) ||
                clientPid != worker.Id)
                return Failed(sequence, "Conexão recusada: o processo conectado não é o esperado.");

            var report = await ReadEventsAsync(server, sequence, sink, cancellationToken);
            return report ?? Failed(sequence, "O processo de manutenção foi encerrado antes de concluir.");
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

    private static async Task<SequenceReport?> ReadEventsAsync(
        NamedPipeServerStream server, MaintenanceSequence sequence, IRunEventSink sink,
        CancellationToken cancellationToken)
    {
        // Limite geral: soma dos limites de cada etapa + margem.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(sequence.Steps.Aggregate(TimeSpan.FromMinutes(1), (total, s) => total + s.Timeout));

        using var reader = new StreamReader(server, new UTF8Encoding(false));
        try
        {
            while (await reader.ReadLineAsync(cts.Token) is { } line)
            {
                switch (RunEventSerializer.TryDeserialize(line))
                {
                    case RunCompletedEvent completed when completed.Report.SequenceId == sequence.Id:
                        return completed.Report;
                    case RunCompletedEvent or null:
                        continue;
                    case var runEvent:
                        sink.Publish(runEvent);
                        break;
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failed(sequence, "O processo de manutenção excedeu o tempo limite.");
        }
        catch (IOException)
        {
            // Canal fechado inesperadamente; tratado abaixo como término sem conclusão.
        }

        return null;
    }

    private static SequenceReport Failed(MaintenanceSequence sequence, string message) =>
        SequenceReport.WithoutRunning(sequence.Id, SequenceOutcome.Failed, message);
}
