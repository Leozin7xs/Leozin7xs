using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace WPSupportTools.Core.Telemetry;

/// <summary>Laboratório e unidade deste computador, preenchidos no primeiro acesso.</summary>
public sealed record UnitInfo(
    [property: JsonPropertyName("laboratorio")] string Laboratory,
    [property: JsonPropertyName("unidade")] string Unit)
{
    /// <summary>Laboratórios do grupo (lista fechada, aparece como seleção na tela).</summary>
    public static IReadOnlyList<string> Laboratories { get; } = ["Santa Helena", "Widal", "Endocrimeta"];

    public const int MinUnitLength = 2;
    public const int MaxUnitLength = 60;

    [JsonIgnore]
    public string DisplayName => $"{Laboratory} — {Unit}";

    /// <summary>Valida e padroniza o que foi digitado. Retorna a mensagem de erro, ou null se estiver certo.</summary>
    public static string? TryCreate(string? laboratory, string? unit, out UnitInfo? info)
    {
        info = null;
        var lab = Laboratories.FirstOrDefault(l => string.Equals(l, laboratory?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (lab is null)
            return "Selecione o laboratório.";

        var name = NormalizeUnit(unit);
        if (name.Length < MinUnitLength)
            return "Digite o nome da unidade.";
        if (name.Length > MaxUnitLength)
            return $"O nome da unidade pode ter no máximo {MaxUnitLength} letras.";

        info = new UnitInfo(lab, name);
        return null;
    }

    /// <summary>"  menino   DEUS " → "Menino Deus" (para a mesma unidade não aparecer de dois jeitos no painel).</summary>
    public static string NormalizeUnit(string? unit)
    {
        var collapsed = Regex.Replace((unit ?? "").Trim(), @"\s+", " ");
        var words = collapsed.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select((w, i) =>
            i > 0 && LowercaseWords.Contains(w.ToLowerInvariant())
                ? w.ToLowerInvariant()
                : w.Length <= 1 ? w.ToUpperInvariant() : char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant());
        return string.Join(' ', words);
    }

    private static readonly HashSet<string> LowercaseWords = ["de", "da", "do", "das", "dos", "e"];
}

/// <summary>
/// Guarda a unidade do computador. Fica em %ProgramData% (vale para todos os usuários do PC);
/// se não for possível gravar lá, fica na pasta do usuário.
/// </summary>
public sealed class UnitStore(string machinePath, string userPath)
{
    public const string FileName = "unidade.json";

    public static UnitStore Default { get; } = new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "WPSupportTools", FileName),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WPSupportTools", FileName));

    public UnitInfo? Load() => Read(machinePath) ?? Read(userPath);

    /// <summary>Grava; retorna onde gravou.</summary>
    public string Save(UnitInfo info)
    {
        var json = JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true });
        try
        {
            Write(machinePath, json);
            return machinePath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Write(userPath, json);
            return userPath;
        }
    }

    private static void Write(string path, string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
    }

    private static UnitInfo? Read(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;
            var stored = JsonSerializer.Deserialize<UnitInfo>(File.ReadAllText(path));
            return UnitInfo.TryCreate(stored?.Laboratory, stored?.Unit, out var info) is null ? info : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }
}
