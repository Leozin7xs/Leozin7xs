using System.IO;
using System.IO.Pipes;
using System.Text;
using WPSupportTools.Core.Execution;
using WPSupportTools.Core.Logging;
using WPSupportTools.Core.Procedures;
using WPSupportTools.Core.Service;

namespace WPSupportTools.App.Elevation;

/// <summary>
/// Pede as etapas de administrador ao serviço do Windows (instalado pelo técnico), sem senha.
/// O serviço valida o pedido contra a lista fechada do programa antes de executar.
/// </summary>
internal sealed class ServiceStepRunner
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(3);

    /// <summary>Null se não conseguiu falar com o serviço (aí a interface usa o UAC).</summary>
    public async Task<IReadOnlyList<ProcedureResult>?> TryRunAsync(
        MaintenanceSequence sequence, IReadOnlyList<int> stepIndices, IReadOnlyDictionary<string, string> state,
        IRunEventSink sink, CancellationToken cancellationToken)
    {
        var pipe = new NamedPipeClientStream(".", ServiceConstants.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            using var connect = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connect.CancelAfter(ConnectTimeout);
            await pipe.ConnectAsync(connect.Token);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or TimeoutException or UnauthorizedAccessException)
        {
            await pipe.DisposeAsync();
            return null;
        }

        await using (pipe)
        {
            var request = ElevatedRequest.For(sequence, stepIndices, state);
            await using (var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true })
                await writer.WriteLineAsync(request.ToJson());

            return await ElevatedProtocol.RunAndCompleteAsync(sequence, stepIndices, sink,
                forwarding => ElevatedProtocol.ReadEventsAsync(pipe, sequence, stepIndices, forwarding, cancellationToken));
        }
    }
}

/// <summary>Usa o serviço quando ele está instalado e rodando; senão, o aviso do UAC.</summary>
internal sealed class ElevatedRunnerSelector(IAppLog log) : IElevatedStepRunner
{
    private readonly ServiceStepRunner _service = new();
    private readonly UacElevatedStepRunner _uac = new();

    public async Task<IReadOnlyList<ProcedureResult>> RunAsync(
        MaintenanceSequence sequence, IReadOnlyList<int> stepIndices, IReadOnlyDictionary<string, string> state,
        IRunEventSink sink, CancellationToken cancellationToken)
    {
        if (Service.Installer.IsServiceRunning())
        {
            var results = await _service.TryRunAsync(sequence, stepIndices, state, sink, cancellationToken);
            if (results is not null)
                return results;
            log.Write(LogLevel.Warning, "Serviço do WP Support Tools não respondeu; pedindo permissão pelo Windows (UAC).");
        }

        return await _uac.RunAsync(sequence, stepIndices, state, sink, cancellationToken);
    }
}
