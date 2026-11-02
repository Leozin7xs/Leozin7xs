using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using WPSupportTools.App.Elevation;
using WPSupportTools.Core.Execution;
using WPSupportTools.Core.Logging;
using WPSupportTools.Core.Service;

namespace WPSupportTools.App.Service;

/// <summary>
/// Recebe pedidos da interface pelo canal local. Qualquer usuário do computador pode pedir, mas o
/// serviço só executa etapas de administrador de sequências do catálogo (validadas por
/// <see cref="ElevatedRequest.TryParse"/>) — nunca um comando.
/// </summary>
internal sealed class ServicePipeServer(IAppLog log, SemaphoreSlim maintenance)
{
    private static readonly TimeSpan ReadRequestTimeout = TimeSpan.FromSeconds(10);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream server;
            try
            {
                server = CreateServer();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                log.Write(LogLevel.Error, $"Não foi possível abrir o canal do serviço: {ex.Message}");
                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken).ContinueWith(_ => { });
                continue;
            }

            try
            {
                await server.WaitForConnectionAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                await server.DisposeAsync();
                return;
            }

            _ = Task.Run(() => HandleAsync(server, cancellationToken), cancellationToken);
        }
    }

    private static NamedPipeServerStream CreateServer()
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            PipeAccessRights.ReadWrite | PipeAccessRights.Synchronize, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(ServiceConstants.PipeName, PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous,
            0, 0, security);
    }

    private async Task HandleAsync(NamedPipeServerStream server, CancellationToken cancellationToken)
    {
        await using (server)
        {
            var clientPid = NativeMethods.GetNamedPipeClientProcessId(server.SafePipeHandle, out var pid) ? pid : 0;
            var writer = new StreamWriter(server, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
            var sink = new PipeSink(writer);

            string? line;
            try
            {
                using var reader = new StreamReader(server, new UTF8Encoding(false), leaveOpen: true);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(ReadRequestTimeout);
                line = await reader.ReadLineAsync(timeout.Token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException)
            {
                return;
            }

            if (line is null)
                return;

            if (!ElevatedRequest.TryParse(line, out var request, out var sequence, out var error))
            {
                log.Write(LogLevel.Warning, $"Pedido recusado (processo {clientPid}): {error}");
                sink.Publish(new LogEvent(LogLevel.Error, $"O serviço recusou o pedido: {error}"));
                return;
            }

            log.Write(LogLevel.Info, $"Pedido do processo {clientPid}: {request.SequenceId}, etapas {string.Join(",", request.Steps)}.");

            await maintenance.WaitAsync(cancellationToken);
            try
            {
                var results = await new SequenceRunner().RunStepsAsync(sequence, request.Steps, sink, cancellationToken, request.State);
                sink.Publish(new StepsCompletedEvent(request.Steps, results));
                log.Write(LogLevel.Info, $"Concluído: {string.Join(", ", results.Select(r => r.Outcome))}.");
            }
            finally
            {
                maintenance.Release();
            }
        }
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
                    // A interface foi fechada; a manutenção continua até o fim (o spooler nunca fica parado).
                    _disconnected = true;
                }
            }
        }
    }
}
