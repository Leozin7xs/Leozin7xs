using System.Text.Json.Serialization;
using WPSupportTools.Core.Logging;
using WPSupportTools.Core.Procedures;

namespace WPSupportTools.Core.Execution;

/// <summary>
/// Eventos emitidos durante a execução de uma sequência. São usados para atualizar a tela,
/// escrever o log e, no modo elevado, trafegar entre o processo administrador e a interface.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(StepStartedEvent), "stepStarted")]
[JsonDerivedType(typeof(StepFinishedEvent), "stepFinished")]
[JsonDerivedType(typeof(LogEvent), "log")]
[JsonDerivedType(typeof(StepsCompletedEvent), "stepsCompleted")]
public abstract record RunEvent;

public sealed record StepStartedEvent(int Index, string ProcedureId) : RunEvent;

public sealed record StepFinishedEvent(int Index, string ProcedureId, ProcedureResult Result) : RunEvent;

public sealed record LogEvent(LogLevel Level, string Message) : RunEvent;

/// <summary>Último evento enviado pelo processo elevado: o resultado de cada etapa que ele recebeu.</summary>
public sealed record StepsCompletedEvent(IReadOnlyList<int> Indices, IReadOnlyList<ProcedureResult> Results) : RunEvent;

public interface IRunEventSink
{
    void Publish(RunEvent runEvent);
}

public sealed class DelegateRunEventSink(Action<RunEvent> publish) : IRunEventSink
{
    public void Publish(RunEvent runEvent) => publish(runEvent);
}
