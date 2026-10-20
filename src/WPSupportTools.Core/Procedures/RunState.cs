namespace WPSupportTools.Core.Procedures;

/// <summary>
/// Informações que uma etapa deixa para as seguintes decidirem se são necessárias
/// (ex.: "a fila está travada"). Existe só durante uma execução e não é gravada em lugar nenhum.
/// </summary>
public sealed class RunState
{
    private readonly Dictionary<string, bool> _flags = new(StringComparer.Ordinal);

    public void Set(string key, bool value) => _flags[key] = value;

    public bool IsSet(string key) => _flags.TryGetValue(key, out var value) && value;
}
