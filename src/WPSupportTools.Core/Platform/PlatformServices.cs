namespace WPSupportTools.Core.Platform;

/// <summary>Conjunto de acessos ao Windows usados pelos procedimentos. Nos testes, é substituído por versões falsas.</summary>
public sealed record PlatformServices(
    IWindowsServices Services,
    IPrintQueue PrintQueue,
    IProcessLauncher Processes,
    Func<DateTime> Now,
    IPrinterCatalog Printers,
    IPrinterPreferences Preferences,
    IRawPrinter Raw)
{
    private static readonly WinSpool Spool = new();

    public static PlatformServices Windows { get; } = new(
        new WindowsServices(),
        new WmiPrintQueue(),
        new WindowsProcessLauncher(),
        () => DateTime.Now,
        new WmiPrinterCatalog(),
        Spool,
        Spool);
}
