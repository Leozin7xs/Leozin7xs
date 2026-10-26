namespace WPSupportTools.Core.Platform;

/// <summary>Impressora instalada no Windows (somente o necessário para identificá-la).</summary>
public sealed record InstalledPrinter(string Name, string DriverName, string PortName, bool IsOffline, bool IsDefault);

/// <summary>Impressoras instaladas (somente leitura).</summary>
public interface IPrinterCatalog
{
    IReadOnlyList<InstalledPrinter> List();
}

/// <summary>
/// Preferências de impressão do usuário atual para uma impressora (as mesmas da tela
/// "Preferências de impressão"), no formato DEVMODE do Windows. Não exige administrador.
/// </summary>
public interface IPrinterPreferences
{
    byte[] Read(string printerName);

    /// <summary>Grava; o próprio driver valida e completa os dados antes de gravar.</summary>
    void Write(string printerName, byte[] devMode);
}

/// <summary>Envia comandos diretamente à impressora (RAW), pela fila do Windows.</summary>
public interface IRawPrinter
{
    void Send(string printerName, string documentName, byte[] data);
}
