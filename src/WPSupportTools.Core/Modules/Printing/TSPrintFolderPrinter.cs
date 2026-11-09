using System.Text;
using System.Text.Json;

namespace WPSupportTools.Core.Modules.Printing;

/// <summary>
/// Lê e troca, no arquivo de impressoras do TSPrint, a impressora de uma pasta (ex.: "root" = A4, "etiq" = etiquetas).
/// Formato (print_doc_cnf.data):
/// <c>[{"ID":3,"FOLDER":"etiq","PRINTERNAME":"ZDesigner TLP 2844 (Copiar 5)","DOWNLOAD":"Sim","PRINT":"Sim"}, ...]</c>
/// </summary>
public static class TSPrintFolderPrinter
{
    private static readonly char[] TrimChars = ['\uFEFF', ' ', '\r', '\n', '\t'];

    /// <summary>A pasta tem um dos nomes aceitos (sem diferenciar maiúsculas).</summary>
    public static bool IsFolder(string? folder, IReadOnlyCollection<string> names) =>
        folder is not null && names.Contains(folder.Trim(), StringComparer.OrdinalIgnoreCase);

    /// <summary>Nome da impressora da pasta, ou null se não houver / o arquivo não for reconhecido.</summary>
    public static string? PrinterOf(string? config, IReadOnlyCollection<string> folders)
    {
        if (string.IsNullOrWhiteSpace(config))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(config.Trim(TrimChars));
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return null;

            foreach (var entry in doc.RootElement.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object ||
                    !TryGetString(entry, "FOLDER", out var folder) ||
                    !IsFolder(folder, folders))
                    continue;

                return TryGetString(entry, "PRINTERNAME", out var name) && !string.IsNullOrWhiteSpace(name)
                    ? name.Trim()
                    : null;
            }
        }
        catch (JsonException)
        {
        }
        return null;
    }

    /// <summary>
    /// O mesmo arquivo com a impressora da pasta trocada por <paramref name="printerName"/>.
    /// Só o valor de PRINTERNAME dessa pasta muda: o resto do texto fica exatamente igual.
    /// Null se não houver a pasta com PRINTERNAME ou o arquivo não for reconhecido.
    /// </summary>
    public static string? WithPrinter(string? config, IReadOnlyCollection<string> folders, string printerName)
    {
        if (string.IsNullOrWhiteSpace(config) || string.IsNullOrWhiteSpace(printerName))
            return null;

        var bytes = Encoding.UTF8.GetBytes(config.TrimStart('﻿'));
        try
        {
            var reader = new Utf8JsonReader(bytes);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray)
                return null;

            string? property = null, folder = null;
            (int Start, int Length)? nameToken = null;

            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.StartObject when reader.CurrentDepth == 1:
                        folder = null;
                        nameToken = null;
                        break;

                    case JsonTokenType.PropertyName when reader.CurrentDepth == 2:
                        property = reader.GetString();
                        break;

                    case JsonTokenType.String when reader.CurrentDepth == 2 && property is not null:
                        if (string.Equals(property, "FOLDER", StringComparison.OrdinalIgnoreCase))
                            folder = reader.GetString();
                        else if (string.Equals(property, "PRINTERNAME", StringComparison.OrdinalIgnoreCase))
                            nameToken = ((int)reader.TokenStartIndex, reader.ValueSpan.Length + 2);   // com as aspas
                        property = null;
                        break;

                    case JsonTokenType.EndObject when reader.CurrentDepth == 1:
                        if (!IsFolder(folder, folders))
                            break;
                        if (nameToken is not { } token)
                            return null;

                        var result =
                            Encoding.UTF8.GetString(bytes, 0, token.Start) +
                            Quote(printerName.Trim()) +
                            Encoding.UTF8.GetString(bytes, token.Start + token.Length, bytes.Length - token.Start - token.Length);
                        return PrinterOf(result, folders) == printerName.Trim() ? result : null;

                    default:
                        if (reader.CurrentDepth == 2)
                            property = null;
                        break;
                }
            }
        }
        catch (JsonException)
        {
        }
        return null;
    }

    /// <summary>A pasta existe no arquivo (com ou sem impressora).</summary>
    public static bool HasFolder(string? config, IReadOnlyCollection<string> folders) =>
        Entries(config)?.Any(e => TryGetString(e, "FOLDER", out var f) && IsFolder(f, folders)) == true;

    /// <summary>
    /// O mesmo arquivo com uma pasta nova no fim da lista (o resto fica idêntico). A entrada nova segue
    /// os campos das que já existem (ex.: "girar 180°" nas versões novas do TSPrint), com ID seguinte,
    /// DOWNLOAD "Sim" e PRINT "Sim"/"Não". Null se o arquivo não for uma lista reconhecida.
    /// </summary>
    public static string? WithFolderAdded(string? config, string folder, string printerName, bool print = true)
    {
        var text = string.IsNullOrWhiteSpace(config) ? "[]" : config.Trim(TrimChars);
        var entries = Entries(text);
        if (entries is null || !text.EndsWith(']'))
            return null;

        var template = entries.FirstOrDefault(e => e.ValueKind == JsonValueKind.Object);
        var nextId = entries.Select(e => e.ValueKind == JsonValueKind.Object && e.TryGetProperty("ID", out var id) &&
                                          id.TryGetInt32(out var n) ? n : 0).DefaultIfEmpty(0).Max() + 1;

        var fields = new List<string>();
        var keys = template.ValueKind == JsonValueKind.Object
            ? template.EnumerateObject().Select(p => p.Name).ToList()
            : ["ID", "FOLDER", "PRINTERNAME", "DOWNLOAD", "PRINT"];
        foreach (var key in keys)
        {
            var value = key.ToUpperInvariant() switch
            {
                "ID" => nextId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "FOLDER" => Quote(folder),
                "PRINTERNAME" => Quote(printerName.Trim()),
                "DOWNLOAD" => Quote("Sim"),
                "PRINT" => Quote(print ? "Sim" : "Não"),
                _ => template.GetProperty(key).GetRawText(),   // demais campos: igual às outras pastas
            };
            fields.Add($"{Quote(key)}:{value}");
        }

        var entry = "{" + string.Join(",", fields) + "}";
        var body = text[..^1].TrimEnd();
        var result = body + (entries.Count > 0 ? "," : "") + entry + "]";
        return Entries(result) is not null ? result : null;
    }

    private static List<JsonElement>? Entries(string? config)
    {
        if (string.IsNullOrWhiteSpace(config))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(config.Trim(TrimChars));
            return doc.RootElement.ValueKind == JsonValueKind.Array
                ? doc.RootElement.EnumerateArray().Select(e => e.Clone()).ToList()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Texto JSON no estilo do TSPrint: acentos e símbolos fora do ASCII como ã.</summary>
    private static string Quote(string value)
    {
        var sb = new StringBuilder("\"");
        foreach (var c in value)
        {
            if (c is '"' or '\\')
                sb.Append('\\').Append(c);
            else if (c < 0x20 || c > 0x7E)
                sb.Append("\\u").Append(((int)c).ToString("X4"));
            else
                sb.Append(c);
        }
        return sb.Append('"').ToString();
    }

    private static bool TryGetString(JsonElement entry, string property, out string value)
    {
        foreach (var p in entry.EnumerateObject())
        {
            if (string.Equals(p.Name, property, StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.String)
            {
                value = p.Value.GetString() ?? "";
                return true;
            }
        }
        value = "";
        return false;
    }
}
