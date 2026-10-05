using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace WPSupportTools.Core.Execution;

/// <summary>
/// Argumentos aceitos pelo modo elevado. O processo administrador recebe apenas o
/// identificador de uma sequência já embutida no programa — nunca um comando.
/// </summary>
public sealed partial record WorkerArguments(string SequenceId, string PipeName, int ParentProcessId)
{
    public const string WorkerFlag = "--worker";

    public static bool IsWorkerInvocation(IReadOnlyList<string> args) =>
        args.Count > 0 && args[0] == WorkerFlag;

    public string[] ToArgumentList() =>
        [WorkerFlag, "--sequence", SequenceId, "--pipe", PipeName, "--parent", ParentProcessId.ToString()];

    public string ToCommandLine() => string.Join(' ', ToArgumentList());

    public static bool TryParse(IReadOnlyList<string> args, [NotNullWhen(true)] out WorkerArguments? parsed)
    {
        parsed = null;

        // Formato exato e completo; qualquer variação é rejeitada.
        if (args.Count != 7 || args[0] != WorkerFlag || args[1] != "--sequence" ||
            args[3] != "--pipe" || args[5] != "--parent")
            return false;

        var sequenceId = args[2];
        var pipeName = args[4];

        if (!SequenceIdPattern().IsMatch(sequenceId) || !PipeNamePattern().IsMatch(pipeName))
            return false;

        if (!int.TryParse(args[6], out var parentPid) || parentPid <= 0)
            return false;

        parsed = new WorkerArguments(sequenceId, pipeName, parentPid);
        return true;
    }

    public static string NewPipeName() => $"WPSupportTools-{Guid.NewGuid():N}";

    [GeneratedRegex("^[a-z]+(\\.[a-z-]+)+$")]
    private static partial Regex SequenceIdPattern();

    [GeneratedRegex("^WPSupportTools-[0-9a-f]{32}$")]
    private static partial Regex PipeNamePattern();
}
