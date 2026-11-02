using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using WPSupportTools.Core.Modules;
using WPSupportTools.Core.Procedures;

namespace WPSupportTools.Core.Execution;

/// <summary>
/// Pedido enviado ao serviço do Windows (ou ao processo elevado) para executar etapas de administrador.
/// Contém só o ID de uma sequência embutida no programa, os números das etapas e informações que a
/// sequência permite (ex.: impressoras travadas) — nunca um comando.
/// <see cref="StepIds"/> repete o nome de cada etapa: se a interface e o serviço estiverem em versões
/// diferentes (ex.: logo após uma atualização), o pedido é recusado em vez de executar a etapa errada.
/// </summary>
public sealed record ElevatedRequest(
    string SequenceId, IReadOnlyList<int> Steps, IReadOnlyList<string> StepIds, IReadOnlyDictionary<string, string> State)
{
    public static ElevatedRequest For(MaintenanceSequence sequence, IReadOnlyList<int> steps, IReadOnlyDictionary<string, string> state) =>
        new(sequence.Id, steps, steps.Select(i => sequence.Steps[i].Id).ToList(), state);

    private const int MaxSteps = 20;
    private const int MaxStateValueLength = 2048;
    public const int MaxLineLength = 64 * 1024;

    public string ToJson() => JsonSerializer.Serialize(this);

    /// <summary>Lê e valida o pedido. Só aceita sequências do catálogo e etapas que exigem administrador.</summary>
    public static bool TryParse(string json, [NotNullWhen(true)] out ElevatedRequest? request,
        [NotNullWhen(true)] out MaintenanceSequence? sequence, out string error)
    {
        request = null;
        sequence = null;

        if (json.Length > MaxLineLength)
        {
            error = "Pedido grande demais.";
            return false;
        }

        try
        {
            request = JsonSerializer.Deserialize<ElevatedRequest>(json);
        }
        catch (JsonException)
        {
            request = null;
        }

        if (request?.SequenceId is null || request.Steps is null || request.StepIds is null || request.State is null)
        {
            error = "Pedido inválido.";
            request = null;
            return false;
        }

        if (!SequenceCatalog.TryCreate(request.SequenceId, out sequence))
        {
            error = $"Sequência desconhecida: {request.SequenceId}.";
            request = null;
            return false;
        }

        if (!StepsAreValid(sequence, request.Steps, request.State))
        {
            error = "Etapas ou informações não permitidas para esta sequência.";
            request = null;
            sequence = null;
            return false;
        }

        var steps = sequence.Steps;
        var expected = request.Steps.Select(i => steps[i].Id);
        if (!expected.SequenceEqual(request.StepIds))
        {
            error = "As etapas pedidas não conferem com esta versão do serviço (versões diferentes). Feche e abra o programa.";
            request = null;
            sequence = null;
            return false;
        }

        error = "";
        return true;
    }

    /// <summary>
    /// Etapas existentes, em ordem crescente, sem repetição, todas de administrador; informações só
    /// das chaves que a sequência declara como permitidas.
    /// </summary>
    public static bool StepsAreValid(
        MaintenanceSequence sequence, IReadOnlyList<int> steps, IReadOnlyDictionary<string, string> state)
    {
        if (steps.Count == 0 || steps.Count > MaxSteps)
            return false;

        for (var k = 0; k < steps.Count; k++)
        {
            var index = steps[k];
            if (index < 0 || index >= sequence.Steps.Count || (k > 0 && index <= steps[k - 1]))
                return false;
            if (!sequence.Steps[index].RequiresElevation)
                return false;
        }

        return state.All(kv => sequence.TransferableStateKeys.Contains(kv.Key) &&
                               kv.Value is not null && kv.Value.Length <= MaxStateValueLength);
    }
}
