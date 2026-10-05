using System.Text.Json;
using System.Text.Json.Serialization;

namespace WPSupportTools.Core.Execution;

/// <summary>Formato das mensagens entre o processo elevado e a interface: uma linha JSON por evento.</summary>
public static class RunEventSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(RunEvent runEvent) => JsonSerializer.Serialize(runEvent, Options);

    /// <summary>Retorna null se a linha não for um evento válido.</summary>
    public static RunEvent? TryDeserialize(string line)
    {
        try
        {
            return JsonSerializer.Deserialize<RunEvent>(line, Options);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return null;
        }
    }
}
