using System.Text.Json;

namespace WPSupportTools.Core.Modules.Printing.Labels;

/// <summary>
/// Lê, do arquivo de impressoras do TSPrint, em qual impressora ele imprime as etiquetas.
/// Formato (print_doc_cnf.data):
/// <c>[{"ID":3,"FOLDER":"etiq","PRINTERNAME":"ZDesigner TLP 2844 (Copiar 5)","DOWNLOAD":"Sim","PRINT":"Sim"}, ...]</c>
/// </summary>
public static class TSPrintLabelPrinter
{
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
                    !string.Equals(folder.Trim(), PrintingConstants.TSPrintLabelFolder, StringComparison.OrdinalIgnoreCase))
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
