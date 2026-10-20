namespace WPSupportTools.Core.Platform;

/// <summary>Pasta onde o Windows guarda os trabalhos da fila de impressão.</summary>
public interface ISpoolDirectory
{
    /// <summary>Pasta padrão do Windows: %SystemRoot%\System32\spool\PRINTERS.</summary>
    string StandardPath { get; }

    /// <summary>Pasta configurada no Windows (leitura do registro). Null se não houver valor configurado.</summary>
    string? ConfiguredPath();
}
