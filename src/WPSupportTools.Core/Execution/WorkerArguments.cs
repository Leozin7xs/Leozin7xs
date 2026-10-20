using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;
using WPSupportTools.Core.Procedures;

namespace WPSupportTools.Core.Execution;

/// <summary>
/// Argumentos aceitos pelo modo elevado. O processo administrador recebe apenas o
/// identificador de uma sequência já embutida no programa e os números das etapas — nunca um comando.
/// </summary>
public sealed partial record WorkerArguments(
    string SequenceId, IReadOnlyList<int> StepIndices, string PipeName, int ParentProcessId)
{
    public const string WorkerFlag = "--worker";

    public static bool IsWorkerInvocation(IReadOnlyList<string> args) =>
        args.Count > 0 && args[0] == WorkerFlag;

    public string[] ToArgumentList() =>
    [
        WorkerFlag,
        "--sequence", SequenceId,
        "--steps", string.Join(',', StepIndices.Select(i => i.ToString(CultureInfo.InvariantCulture))),
        "--pipe", PipeName,
        "--parent", ParentProcessId.ToString(CultureInfo.InvariantCulture),
    ];

    public string ToCommandLine() => string.Join(' ', ToArgumentList());

    public static bool TryParse(IReadOnlyList<string> args, [NotNullWhen(true)] out WorkerArguments? parsed)
    {
        parsed = null;

        // Formato exato e completo; qualquer variação é rejeitada.
        if (args.Count != 9 || args[0] != WorkerFlag || args[1] != "--sequence" || args[3] != "--steps" ||
            args[5] != "--pipe" || args[7] != "--parent")
            return false;

        if (!SequenceIdPattern().IsMatch(args[2]) || !StepsPattern().IsMatch(args[4]) ||
            !PipeNamePattern().IsMatch(args[6]))
            return false;

        if (!int.TryParse(args[8], NumberStyles.None, CultureInfo.InvariantCulture, out var parentPid) || parentPid <= 0)
            return false;

        var steps = args[4].Split(',').Select(s => int.Parse(s, CultureInfo.InvariantCulture)).ToArray();
        parsed = new WorkerArguments(args[2], steps, args[6], parentPid);
        return true;
    }

    /// <summary>
    /// O processo elevado só executa etapas que existem na sequência, em ordem crescente,
    /// sem repetição, e que realmente exigem administrador.
    /// </summary>
    public bool StepsAreValidFor(MaintenanceSequence sequence)
    {
        if (StepIndices.Count == 0)
            return false;

        for (var k = 0; k < StepIndices.Count; k++)
        {
            var index = StepIndices[k];
            if (index < 0 || index >= sequence.Steps.Count || (k > 0 && index <= StepIndices[k - 1]))
                return false;
            if (!sequence.Steps[index].RequiresElevation)
                return false;
        }

        return true;
    }

    public static string NewPipeName() => $"WPSupportTools-{Guid.NewGuid():N}";

    [GeneratedRegex("^[a-z]+(\\.[a-z-]+)+$")]
    private static partial Regex SequenceIdPattern();

    [GeneratedRegex("^[0-9]{1,2}(,[0-9]{1,2}){0,19}$")]
    private static partial Regex StepsPattern();

    [GeneratedRegex("^WPSupportTools-[0-9a-f]{32}$")]
    private static partial Regex PipeNamePattern();
}
