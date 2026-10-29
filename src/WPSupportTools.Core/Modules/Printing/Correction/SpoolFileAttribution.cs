using System.Text;

namespace WPSupportTools.Core.Modules.Printing.Correction;

/// <summary>
/// Descobre de qual impressora é um documento da fila, procurando o nome exato da impressora
/// dentro do arquivo .SHD (onde o Windows guarda os dados do documento). Não lê nem registra
/// nome do documento ou do usuário.
/// </summary>
public static class SpoolFileAttribution
{
    /// <summary>
    /// Nome da impressora do documento, ou null se nenhum (ou mais de um) nome de impressora aparecer.
    /// Nomes mais longos são procurados primeiro, para "ZDesigner TLP 2844 (Copiar 2)" não ser
    /// confundido com "ZDesigner TLP 2844".
    /// </summary>
    public static string? PrinterOf(ReadOnlySpan<byte> shadowFile, IEnumerable<string> printerNames)
    {
        var data = shadowFile.ToArray();
        string? match = null;

        foreach (var name in printerNames.Where(n => n.Length > 0).Distinct().OrderByDescending(n => n.Length))
        {
            if (!RemoveOccurrences(data, Encoding.Unicode.GetBytes(name + "\0")))
                continue;
            if (match is not null)
                return null;   // dois nomes diferentes: ambíguo, não adivinha
            match = name;
        }

        return match;
    }

    /// <summary>Apaga (na cópia) cada ocorrência alinhada do texto, para nomes menores não casarem dentro dele.</summary>
    private static bool RemoveOccurrences(byte[] data, byte[] pattern)
    {
        var found = false;
        var offset = 0;
        while (offset <= data.Length - pattern.Length)
        {
            var index = data.AsSpan(offset).IndexOf(pattern);
            if (index < 0)
                break;

            var position = offset + index;
            if (position % 2 == 0)
            {
                data.AsSpan(position, pattern.Length).Fill(0xFF);
                found = true;
                offset = position + pattern.Length;
            }
            else
            {
                offset = position + 1;
            }
        }

        return found;
    }
}
