using WPSupportTools.Core.Procedures;

namespace WPSupportTools.Core.Execution;

/// <summary>Ponto de entrada da interface para executar uma sequência.</summary>
public interface ISequenceExecutor
{
    Task<SequenceReport> RunAsync(MaintenanceSequence sequence, IRunEventSink sink, CancellationToken cancellationToken);
}

/// <summary>
/// Roda a sequência neste processo; etapas de administrador vão para o <see cref="IElevatedStepRunner"/>.
/// </summary>
public sealed class LocalSequenceExecutor(IElevatedStepRunner? elevatedRunner) : ISequenceExecutor
{
    public Task<SequenceReport> RunAsync(
        MaintenanceSequence sequence, IRunEventSink sink, CancellationToken cancellationToken) =>
        // Task.Run evita que um procedimento síncrono trave a interface.
        Task.Run(() => new SequenceRunner(elevatedRunner).RunAsync(sequence, sink, cancellationToken), cancellationToken);
}
