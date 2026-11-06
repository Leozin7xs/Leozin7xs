using System.Text;
using WPSupportTools.Core.Modules.Printing;

namespace WPSupportTools.Core.Platform;

/// <summary>
/// Configuração de impressoras do TSPrint (UniLab). Só o arquivo de impressoras
/// (<see cref="PrintingConstants.TSPrintPrinterConfigPath"/>) é usado: o arquivo geral,
/// que tem usuário e senha, nunca é lido nem alterado.
/// </summary>
public interface ITSPrintConfig
{
    /// <summary>Conteúdo do arquivo, ou null se ele não existir neste computador.</summary>
    string? ReadPrinterConfig();

    /// <summary>Grava o arquivo de impressoras, guardando antes uma cópia do atual.</summary>
    void WritePrinterConfig(string content);
}

public sealed class TSPrintConfigFile : ITSPrintConfig
{
    public string? ReadPrinterConfig()
    {
        var path = PrintingConstants.TSPrintPrinterConfigPath;
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    public void WritePrinterConfig(string content)
    {
        var path = PrintingConstants.TSPrintPrinterConfigPath;
        var hasBom = false;

        if (File.Exists(path))
        {
            var original = File.ReadAllBytes(path);
            hasBom = original is [0xEF, 0xBB, 0xBF, ..];

            // Cópia do arquivo anterior, fora da pasta do TSPrint, para desfazer à mão se preciso.
            var backups = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WPSupportTools", "Backups");
            Directory.CreateDirectory(backups);
            File.WriteAllBytes(Path.Combine(backups, $"print_doc_cnf.data.{DateTime.Now:yyyyMMdd-HHmmss}"), original);
        }

        // Grava ao lado e troca de uma vez: o TSPrint nunca vê o arquivo pela metade.
        var temp = path + ".wpst-novo";
        File.WriteAllText(temp, content.TrimStart('﻿'), new UTF8Encoding(hasBom));
        File.Move(temp, path, overwrite: true);
    }
}
