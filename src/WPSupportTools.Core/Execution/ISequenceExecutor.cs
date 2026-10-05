using WPSupportTools.Core.Procedures;

namespace WPSupportTools.Core.Execution;

/// <summary>
/// Onde a sequência é executada. Hoje existem duas formas:
/// no próprio processo (<see cref="InProcessSequenceExecutor"/>) ou em um processo
/// elevado via UAC (implementado no projeto da interface). No futuro, um serviço do
/// Windows instalado pela TI pode ser outra implementação, sem mudar o restante.
/// </summary>
public interface ISequenceExecutor
{
    Task<SequenceReport> RunAsync(MaintenanceSequence sequence, IRunEventSink sink, CancellationToken cancellationToken);
}

public sealed class InProcessSequenceExecutor : ISequenceExecutor
{
    public Task<SequenceReport> RunAsync(
        MaintenanceSequence sequence, IRunEventSink sink, CancellationToken cancellationToken) =>
        // Task.Run evita que um procedimento síncrono trave a interface.
        Task.Run(() => new SequenceRunner().RunAsync(sequence, sink, cancellationToken), cancellationToken);
}
