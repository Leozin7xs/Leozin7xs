using WPSupportTools.Core.Execution;
using WPSupportTools.Core.Logging;

namespace WPSupportTools.Core.Procedures;

/// <summary>O que um procedimento recebe ao ser executado.</summary>
public sealed class ProcedureContext
{
    private readonly IRunEventSink _sink;

    internal ProcedureContext(IRunEventSink sink, bool isElevated)
    {
        _sink = sink;
        IsElevated = isElevated;
    }

    public bool IsElevated { get; }

    /// <summary>Registra um detalhe no log (não aparece na tela do usuário).</summary>
    public void Log(string message, LogLevel level = LogLevel.Info) =>
        _sink.Publish(new LogEvent(level, message));
}
