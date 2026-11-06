using System.Text;
using System.Text.Json;

namespace WPSupportTools.Core.Modules.Printing.Labels;

/// <summary>
/// Lê e troca, no arquivo de impressoras do TSPrint, a impressora em que ele imprime as etiquetas.
/// Formato (print_doc_cnf.data):
/// <c>[{"ID":3,"FOLDER":"etiq","PRINTERNAME":"ZDesigner TLP 2844 (Copiar 5)","DOWNLOAD":"Sim","PRINT":"Sim"}, ...]</c>
/// </summary>
public static class TSPrintLabelPrinter
{
    /// <summary>A pasta do TSPrint é a das etiquetas ("etiq", "etiqueta"...).</summary>
    public static bool IsLabelFolder(string? folder) =>
        folder is not null &&
        PrintingConstants.TSPrintLabelFolders.Contains(folder.Trim(), StringComparer.OrdinalIgnoreCase);

    /// <summary>Nome da impressora da pasta de etiquetas, ou null se não houver / o arquivo não for reconhecido.</summary>
    public static string? From(string? config)
    {
        if (string.IsNullOrWhiteSpace(config))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(config.Trim('﻿', ' ', '\r', '\n', '\t'));
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return null;

            foreach (var entry in doc.RootElement.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object ||
                    !TryGetString(entry, "FOLDER", out var folder) ||
                    !IsLabelFolder(folder))
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
    /// O mesmo arquivo com a impressora da pasta de etiquetas trocada por <paramref name="printerName"/>.
    /// Só o valor de PRINTERNAME dessa pasta muda: o resto do texto fica exatamente igual.
    /// Null se não houver pasta de etiquetas com PRINTERNAME ou o arquivo não for reconhecido.
    /// </summary>
    public static string? WithPrinter(string? config, string printerName)
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
                        if (!IsLabelFolder(folder))
                            break;
                        if (nameToken is not { } token)
                            return null;

                        var result =
                            Encoding.UTF8.GetString(bytes, 0, token.Start) +
                            Quote(printerName.Trim()) +
                            Encoding.UTF8.GetString(bytes, token.Start + token.Length, bytes.Length - token.Start - token.Length);
                        return From(result) == printerName.Trim() ? result : null;

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
