namespace WPSupportTools.Core.Platform;

/// <summary>Impressora instalada no Windows (somente o necessário para identificá-la).</summary>
/// <param name="Problem">O que o Windows diz que precisa de atenção na impressora (ex.: "sem papel"), ou null.</param>
/// <param name="UsbConnected">Porta USB: true com o aparelho conectado, false com a porta sem aparelho
/// (impressora trocada, cabo solto ou desligada); null fora de porta USB ou sem como saber.</param>
public sealed record InstalledPrinter(
    string Name, string DriverName, string PortName, bool IsOffline, bool IsDefault, string? Problem = null,
    bool? UsbConnected = null);

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
