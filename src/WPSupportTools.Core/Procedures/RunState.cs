namespace WPSupportTools.Core.Procedures;

/// <summary>
/// Informações que uma etapa deixa para as seguintes decidirem se são necessárias
/// (ex.: "a fila está travada"). Existe só durante uma execução e não é gravada em lugar nenhum.
/// </summary>
public sealed class RunState
{
    private readonly Dictionary<string, bool> _flags = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _texts = new(StringComparer.Ordinal);

    public void Set(string key, bool value) => _flags[key] = value;

    public bool IsSet(string key) => _flags.TryGetValue(key, out var value) && value;

    public void SetText(string key, string value) => _texts[key] = value;

    public string? GetText(string key) => _texts.GetValueOrDefault(key);

    /// <summary>Textos selecionados para enviar ao processo de administrador.</summary>
    public IReadOnlyDictionary<string, string> TextsFor(IEnumerable<string> keys) =>
        keys.Where(_texts.ContainsKey).ToDictionary(k => k, k => _texts[k], StringComparer.Ordinal);

    public void SetTexts(IReadOnlyDictionary<string, string> texts)
    {
        foreach (var (key, value) in texts)
            _texts[key] = value;
    }
}
