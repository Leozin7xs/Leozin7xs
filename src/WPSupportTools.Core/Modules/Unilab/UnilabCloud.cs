using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace WPSupportTools.Core.Modules.Unilab;

/// <summary>
/// UniLab novo (Unilab Cloud, da Uniware): onde fica, a licença e o arquivo de configuração
/// (<c>config.json</c>), que também guarda o TSPrint embutido nele.
/// </summary>
public static partial class UnilabCloud
{
    public const string Executable = @"C:\Rede_Unilab\Unilab\Unilab.exe";
    public const string ConfigPath = @"C:\rede_unilab\dados\Unilab\data\config.json";

    /// <summary>UniLab antigo (fica na raiz de C:\Rede_Unilab; mesmo nome de processo do novo).</summary>
    public const string LegacyExecutable = @"C:\Rede_Unilab\Unilab.exe";
    public const string ProcessName = "Unilab";

    /// <summary>Servidor do TSPrint embutido (padrão da Uniware).</summary>
    public const string TSPrintBaseUrl = "http://imp.unilab.app.br:3080/tsprint";

    [GeneratedRegex(@"^@\d{5}[A-Za-z0-9._-]{1,60}$")]
    private static partial Regex LicensePattern();

    /// <summary>
    /// Licença digitada pela atendente → formato do UniLab ("@01451exemplo01").
    /// Aceita sem o "@" e com espaços em volta.
    /// </summary>
    public static (string? Value, string? Error) NormalizeLicense(string? typed)
    {
        var text = (typed ?? "").Trim().Replace(" ", "");
        if (text.Length == 0)
            return (null, "Digite a licença.");
        if (!text.StartsWith('@'))
            text = "@" + text;
        return LicensePattern().IsMatch(text)
            ? (text, null)
            : (null, "Licença inválida. Ela começa com @ e 5 números, ex.: @01451exemplo01.");
    }

    /// <summary>Código ADM: os 5 primeiros dígitos da licença.</summary>
    public static string AdminCode(string license) => license.TrimStart('@')[..5];

    /// <summary>Usuário RDP: a licença sem o "@".</summary>
    public static string RdpUser(string license) => license.Replace("@", "");

    /// <summary>
    /// Senha do TSPrint: a do servidor da WP (token do TSPrint antigo, que é a senha em hexadecimal,
    /// como o próprio UniLab lê); sem ele, o padrão da Uniware (código ADM + "aaa").
    /// </summary>
    public static string Password(string license, string? legacyToken)
    {
        if (!string.IsNullOrWhiteSpace(legacyToken) && legacyToken.Length % 2 == 0 && HexPattern().IsMatch(legacyToken))
        {
            var text = Encoding.UTF8.GetString(Convert.FromHexString(legacyToken));
            if (text.Length > 0 && text.All(c => c >= ' ' && c <= '~'))
                return text;
        }
        return AdminCode(license) + "aaa";
    }

    [GeneratedRegex("^[0-9A-Fa-f]+$")]
    private static partial Regex HexPattern();

    /// <summary>Regra de impressão de uma pasta do TSPrint embutido.</summary>
    public sealed record PrinterRule(string Folder, string PrinterName, bool Print);

    public sealed record Result(string Json, IReadOnlyList<string> Changes);

    /// <summary>
    /// O config.json com a licença, o TSPrint ligado (servidor, código ADM, usuário RDP, senha) e as regras
    /// das pastas informadas. Os demais campos ficam como estão; sem arquivo, começa do padrão do UniLab.
    /// Null se o arquivo existir mas não for reconhecido.
    /// </summary>
    public static Result? Apply(string? current, string? license, string? password, IReadOnlyList<PrinterRule> rules)
    {
        JsonObject config;
        if (string.IsNullOrWhiteSpace(current))
        {
            config = Default();
        }
        else
        {
            try
            {
                if (JsonNode.Parse(current.Trim('\uFEFF', ' ', '\r', '\n', '\t')) is not JsonObject parsed)
                    return null;
                config = parsed;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        var changes = new List<string>();
        license ??= Text(config["user"]);
        if (string.IsNullOrWhiteSpace(license))
            return new Result(config.ToJsonString(Json), changes);   // sem licença não há o que derivar

        Set(config, "user", license, "licença", changes);

        if (config["tsprint"] is not JsonObject tsprint)
        {
            tsprint = new JsonObject();
            config["tsprint"] = tsprint;
        }

        Set(tsprint, "enabled", true, "TSPrint ligado", changes);
        Set(tsprint, "baseUrl", TSPrintBaseUrl, "servidor", changes);
        Set(tsprint, "codAdm", AdminCode(license), "código ADM", changes);
        Set(tsprint, "rdpUser", RdpUser(license), "usuário RDP", changes);
        if (password is not null)
            Set(tsprint, "password", password, "senha", changes);

        if (tsprint["pdfRules"] is not JsonArray pdfRules)
        {
            pdfRules = [];
            tsprint["pdfRules"] = pdfRules;
        }

        foreach (var rule in rules)
        {
            var existing = pdfRules.OfType<JsonObject>()
                .FirstOrDefault(r => string.Equals(Text(r["folder"]), rule.Folder, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                pdfRules.Add(new JsonObject
                {
                    ["folder"] = rule.Folder,
                    ["printerName"] = rule.PrinterName,
                    ["download"] = true,
                    ["print"] = rule.Print,
                    ["rotate180"] = false,
                    ["fitToPaper"] = "none",
                });
                changes.Add($"pasta {rule.Folder} → {rule.PrinterName}");
                continue;
            }

            var before = changes.Count;
            Set(existing, "printerName", rule.PrinterName, $"pasta {rule.Folder} → {rule.PrinterName}", changes);
            Set(existing, "download", true, $"pasta {rule.Folder}: baixar", changes);
            Set(existing, "print", rule.Print, $"pasta {rule.Folder}: imprimir", changes);
            if (changes.Count > before + 1)
                changes.RemoveRange(before + 1, changes.Count - before - 1);   // uma linha por pasta
        }

        return new Result(config.ToJsonString(Json), changes);
    }

    /// <summary>Este PC tem o UniLab novo (com o TSPrint embutido)?</summary>
    public static bool IsInstalled(Platform.PlatformServices platform) => platform.Processes.FileExists(Executable);

    /// <summary>O TSPrint embutido já tem senha?</summary>
    public static bool HasPassword(string? current) => Field(current, c => Text(c["tsprint"]?["password"])) is not null;

    /// <summary>Licença gravada no config.json, ou null.</summary>
    public static string? LicenseOf(string? current) => Field(current, c => Text(c["user"]));

    /// <summary>Impressora de uma pasta (root, etiq...) no TSPrint embutido, ou null.</summary>
    public static string? PrinterOf(string? current, IReadOnlyCollection<string> folders) =>
        Field(current, c => (c["tsprint"]?["pdfRules"] as JsonArray)?.OfType<JsonObject>()
            .FirstOrDefault(r => folders.Contains(Text(r["folder"]) ?? "", StringComparer.OrdinalIgnoreCase)) is { } rule
            ? Text(rule["printerName"])
            : null);

    private static string? Field(string? current, Func<JsonObject, string?> read)
    {
        if (string.IsNullOrWhiteSpace(current))
            return null;
        try
        {
            return JsonNode.Parse(current.Trim('\uFEFF', ' ', '\r', '\n', '\t')) is JsonObject c ? read(c) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // O UniLab grava com JSON.stringify(config, null, 2).
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        IndentSize = 2,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static string? Text(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text.Trim() : null;

    private static void Set(JsonObject target, string key, string value, string change, List<string> changes)
    {
        if (Text(target[key]) == value)
            return;
        target[key] = value;
        changes.Add(change);
    }

    private static void Set(JsonObject target, string key, bool value, string change, List<string> changes)
    {
        if (target[key] is JsonValue v && v.TryGetValue<bool>(out var now) && now == value)
            return;
        target[key] = value;
        changes.Add(change);
    }

    /// <summary>Mesmo padrão do UniLab no primeiro uso (createDefaultConfig); o resto ele completa ao abrir.</summary>
    private static JsonObject Default() => new()
    {
        ["user"] = "",
        ["environment"] = "https",
        ["startMaximized"] = false,
        ["restartSessionEnabled"] = false,
        ["chatlabEnabled"] = true,
        ["blockMode"] = "warn",
        ["externalNavigation"] = "deny",
        ["tsprint"] = new JsonObject
        {
            ["enabled"] = false,
            ["baseUrl"] = TSPrintBaseUrl,
            ["pollIntervalSeconds"] = 2,
            ["webcamEnabled"] = true,
            ["scannerEnabled"] = true,
            ["fingerprintEnabled"] = true,
            ["captureCompress"] = false,
            ["captureHashCheck"] = false,
            ["scannerDriver"] = "AUTO",
            ["pdfRules"] = new JsonArray(),
            ["textRules"] = new JsonArray(new JsonObject
            {
                ["extension"] = "txt",
                ["printerName"] = "",
                ["port"] = "LPT1:",
                ["concat"] = true,
                ["useDriver"] = false,
                ["download"] = false,
                ["print"] = false,
            }),
        },
    };
}
