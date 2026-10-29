using WPSupportTools.Core.Procedures;

namespace WPSupportTools.Core.Execution;

/// <summary>
/// Executa, com privilégios de administrador, um bloco de etapas de uma sequência do catálogo.
/// Hoje: um processo elevado via UAC (projeto da interface). No futuro, pode ser um serviço do Windows.
/// </summary>
/// <remarks>
/// Contrato: publica <see cref="StepFinishedEvent"/> para cada índice recebido (inclusive quando
/// a elevação é negada) e devolve um resultado por índice, na mesma ordem.
/// </remarks>
public interface IElevatedStepRunner
{
    Task<IReadOnlyList<ProcedureResult>> RunAsync(
        MaintenanceSequence sequence, IReadOnlyList<int> stepIndices, IReadOnlyDictionary<string, string> state,
        IRunEventSink sink, CancellationToken cancellationToken);
}
