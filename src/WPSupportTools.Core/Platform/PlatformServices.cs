namespace WPSupportTools.Core.Platform;

/// <summary>Conjunto de acessos ao Windows usados pelos procedimentos. Nos testes, é substituído por versões falsas.</summary>
public sealed record PlatformServices(
    IWindowsServices Services,
    IPrintQueue PrintQueue,
    IProcessLauncher Processes,
    Func<DateTime> Now)
{
    public static PlatformServices Windows { get; } = new(
        new WindowsServices(),
        new WmiPrintQueue(),
        new WindowsProcessLauncher(),
        () => DateTime.Now);
}
