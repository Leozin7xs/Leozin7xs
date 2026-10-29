using System.IO;
using System.IO.Pipes;
using System.Text;
using WPSupportTools.Core.Execution;
using WPSupportTools.Core.Modules;

namespace WPSupportTools.App.Elevation;

/// <summary>
/// Lado administrador: roda quando o executável é iniciado com "--worker ...".
/// Não exibe janela, aceita somente etapas de administrador de sequências do
/// <see cref="SequenceCatalog"/> e devolve o progresso para a interface que o iniciou.
/// </summary>
internal static class ElevatedWorkerHost
{
    public static async Task<int> RunAsync(IReadOnlyList<string> args)
    {
        if (!WorkerArguments.TryParse(args, out var parsed))
            return 2;

        if (!SequenceCatalog.TryCreate(parsed.SequenceId, out var sequence) || !parsed.StepsAreValidFor(sequence))
            return 3;

        await using var pipe = new NamedPipeClientStream(".", parsed.PipeName, PipeDirection.Out, PipeOptions.Asynchronous);
        try
        {
            using var connectTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await pipe.ConnectAsync(connectTimeout.Token);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or TimeoutException)
        {
            return 4;
        }

        // Só conversa com a interface que realmente o iniciou.
        if (!NativeMethods.GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var serverPid) ||
            serverPid != parsed.ParentProcessId)
            return 5;

        await using var writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true };
        var sink = new PipeSink(writer);

        // Mesmo que a interface seja fechada no meio, o bloco vai até o fim:
        // interromper no meio poderia deixar o serviço de impressão parado.
        var results = await new SequenceRunner().RunStepsAsync(sequence, parsed.StepIndices, sink, state: parsed.StateOrEmpty);
        sink.Publish(new StepsCompletedEvent(parsed.StepIndices, results));
        return 0;
    }

    private sealed class PipeSink(StreamWriter writer) : IRunEventSink
    {
        private readonly object _gate = new();
        private bool _disconnected;

        public void Publish(RunEvent runEvent)
        {
            lock (_gate)
            {
                if (_disconnected)
                    return;

                try
                {
                    writer.WriteLine(RunEventSerializer.Serialize(runEvent));
                }
                catch (IOException)
                {
                    _disconnected = true;
                }
            }
        }
    }
}
