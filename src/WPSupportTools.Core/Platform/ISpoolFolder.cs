namespace WPSupportTools.Core.Platform;

/// <summary>Pasta onde o Windows guarda os arquivos da fila de impressão (.SPL e .SHD).</summary>
public interface ISpoolFolder
{
    /// <summary>Pasta padrão do Windows: %SystemRoot%\System32\spool\PRINTERS.</summary>
    string StandardPath { get; }

    /// <summary>Pasta configurada no Windows (leitura do registro). Null se não houver valor configurado.</summary>
    string? ConfiguredPath();

    /// <summary>
    /// Nomes das impressoras instaladas, lidos do registro (funciona com o serviço de impressão parado,
    /// quando a lista normal de impressoras não está disponível).
    /// </summary>
    IReadOnlyList<string> InstalledPrinterNames();
}
