using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WPSupportTools.Core.Modules.Printing;

/// <summary>
/// Padrão WP do servidor de impressão no TSPrint (aba Configurações → Conexão), igual em todas as unidades:
/// servidor Compartilhado, 20 processos, intervalo 2 s, URL, código ADM e porta. O usuário RDP vem do
/// Unilab do próprio PC e a senha (token) vem do build.
/// </summary>
public static class TSPrintServerSettings
{
    /// <summary>"Compartilhado" na lista "Servidor" (0 = Privado).</summary>
    public const int SharedServer = 1;
    public const int MaxProcesses = 20;
    public const int IntervalSeconds = 2;
    public const string Url = "http://imp.unilab.app.br/tsprint";
    public const string AdminCode = "01451";
    public const int Port = 3080;

    /// <summary>Valor que o TSPrint traz quando não foi configurado.</summary>
    public const string PlaceholderRdpUser = "00000Cent00";

    public sealed record Result(string Json, IReadOnlyList<string> ChangedFields);

    /// <summary>
    /// O arquivo geral com o padrão aplicado; os outros campos (MainApp, timeouts, versão...) ficam como estão.
    /// Usuário RDP e token só mudam quando há um valor para pôr. Null se o arquivo não for reconhecido.
    /// </summary>
    public static Result? Apply(string? current, string? rdpUser, string? token)
    {
        if (string.IsNullOrWhiteSpace(current))
            return null;

        JsonObject? config;
        try
        {
            config = JsonNode.Parse(current.Trim('\uFEFF', ' ', '\r', '\n', '\t')) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
        if (config is null)
            return null;

        var changed = new List<string>();

        void SetNumber(string key, int value)
        {
            if (config[key] is JsonValue v && v.TryGetValue<int>(out var now) && now == value)
                return;
            config[key] = value;
            changed.Add(key);
        }

        void SetText(string key, string value)
        {
            if (config[key] is JsonValue v && v.TryGetValue<string>(out var now) && now == value)
                return;
            config[key] = value;
            changed.Add(key);
        }

        SetNumber("servidor", SharedServer);
        SetNumber("maxthreads", MaxProcesses);
        SetText("url", Url);
        SetText("codigo_adm", AdminCode);
        SetNumber("porta", Port);
        SetNumber("intervalo", IntervalSeconds);
        if (!string.IsNullOrWhiteSpace(rdpUser))
            SetText("rdp_user", rdpUser.Trim());
        if (!string.IsNullOrWhiteSpace(token))
            SetText("token", token.Trim());

        return new Result(config.ToJsonString(), changed);
    }

    /// <summary>Usuário RDP do arquivo geral, se for um de verdade (não vazio nem o padrão de fábrica).</summary>
    public static string? ConfiguredRdpUser(string? current) => Text(current, "rdp_user") is { } user &&
        !user.Equals(PlaceholderRdpUser, StringComparison.OrdinalIgnoreCase) ? user : null;

    public static bool HasToken(string? current) => Text(current, "token") is not null;

    private static string? Text(string? current, string key)
    {
        if (string.IsNullOrWhiteSpace(current))
            return null;
        try
        {
            return JsonNode.Parse(current.Trim('\uFEFF', ' ', '\r', '\n', '\t'))?[key] is JsonValue v &&
                   v.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text)
                ? text.Trim()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Senha do servidor no formato do TSPrint (token), injetada pelo build no GitHub
    /// (segredo WPST_TSPRINT_TOKEN). Null nas compilações locais.
    /// </summary>
    public static string? BuildToken(Assembly? entry = null) =>
        (entry ?? Assembly.GetEntryAssembly())?
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "TSPrintToken")?.Value is { Length: > 0 } token
            ? token
            : null;
}
