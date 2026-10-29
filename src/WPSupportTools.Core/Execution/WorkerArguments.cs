using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using WPSupportTools.Core.Procedures;

namespace WPSupportTools.Core.Execution;

/// <summary>
/// Argumentos aceitos pelo modo elevado. O processo administrador recebe apenas o identificador de uma
/// sequência já embutida no programa, os números das etapas e, opcionalmente, informações permitidas
/// pela sequência (ex.: nomes das impressoras travadas) — nunca um comando.
/// </summary>
public sealed partial record WorkerArguments(
    string SequenceId,
    IReadOnlyList<int> StepIndices,
    string PipeName,
    int ParentProcessId,
    IReadOnlyDictionary<string, string>? State = null)
{
    public const string WorkerFlag = "--worker";
    private const int MaxStateValueLength = 2048;

    public IReadOnlyDictionary<string, string> StateOrEmpty => State ?? new Dictionary<string, string>();

    public static bool IsWorkerInvocation(IReadOnlyList<string> args) =>
        args.Count > 0 && args[0] == WorkerFlag;

    public string[] ToArgumentList() =>
    [
        WorkerFlag,
        "--sequence", SequenceId,
        "--steps", string.Join(',', StepIndices.Select(i => i.ToString(CultureInfo.InvariantCulture))),
        "--state", Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(StateOrEmpty))),
        "--pipe", PipeName,
        "--parent", ParentProcessId.ToString(CultureInfo.InvariantCulture),
    ];

    public string ToCommandLine() => string.Join(' ', ToArgumentList());

    public static bool TryParse(IReadOnlyList<string> args, [NotNullWhen(true)] out WorkerArguments? parsed)
    {
        parsed = null;

        // Formato exato e completo; qualquer variação é rejeitada.
        if (args.Count != 11 || args[0] != WorkerFlag || args[1] != "--sequence" || args[3] != "--steps" ||
            args[5] != "--state" || args[7] != "--pipe" || args[9] != "--parent")
            return false;

        if (!SequenceIdPattern().IsMatch(args[2]) || !StepsPattern().IsMatch(args[4]) ||
            !StatePattern().IsMatch(args[6]) || !PipeNamePattern().IsMatch(args[8]))
            return false;

        if (!int.TryParse(args[10], NumberStyles.None, CultureInfo.InvariantCulture, out var parentPid) || parentPid <= 0)
            return false;

        Dictionary<string, string>? state;
        try
        {
            state = JsonSerializer.Deserialize<Dictionary<string, string>>(Encoding.UTF8.GetString(Convert.FromBase64String(args[6])));
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return false;
        }

        if (state is null || state.Values.Any(v => v is null || v.Length > MaxStateValueLength))
            return false;

        var steps = args[4].Split(',').Select(s => int.Parse(s, CultureInfo.InvariantCulture)).ToArray();
        parsed = new WorkerArguments(args[2], steps, args[8], parentPid, state);
        return true;
    }

    /// <summary>
    /// O processo elevado só executa etapas que existem na sequência, em ordem crescente,
    /// sem repetição, e que realmente exigem administrador; e só aceita informações que a
    /// sequência declara como permitidas.
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

        return StateOrEmpty.Keys.All(sequence.TransferableStateKeys.Contains);
    }

    public static string NewPipeName() => $"WPSupportTools-{Guid.NewGuid():N}";

    [GeneratedRegex("^[a-z]+(\\.[a-z-]+)+$")]
    private static partial Regex SequenceIdPattern();

    [GeneratedRegex("^[0-9]{1,2}(,[0-9]{1,2}){0,19}$")]
    private static partial Regex StepsPattern();

    [GeneratedRegex("^[A-Za-z0-9+/=]{4,8192}$")]
    private static partial Regex StatePattern();

    [GeneratedRegex("^WPSupportTools-[0-9a-f]{32}$")]
    private static partial Regex PipeNamePattern();
}
