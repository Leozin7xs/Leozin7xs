using WPSupportTools.Core.Modules.Printing;

namespace WPSupportTools.Core.Platform;

/// <summary>
/// Configuração de impressoras do TSPrint (UniLab). Somente leitura, e somente o arquivo
/// de impressoras (<see cref="PrintingConstants.TSPrintPrinterConfigPath"/>): o arquivo geral,
/// que tem usuário e senha, nunca é lido.
/// </summary>
public interface ITSPrintConfig
{
    /// <summary>Conteúdo do arquivo, ou null se ele não existir neste computador.</summary>
    string? ReadPrinterConfig();
}

public sealed class TSPrintConfigFile : ITSPrintConfig
{
    public string? ReadPrinterConfig()
    {
        var path = PrintingConstants.TSPrintPrinterConfigPath;
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }
}
