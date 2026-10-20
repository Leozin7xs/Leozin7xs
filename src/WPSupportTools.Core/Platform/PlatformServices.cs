namespace WPSupportTools.Core.Platform;

/// <summary>Conjunto de acessos ao Windows usados pelos procedimentos. Nos testes, é substituído por versões falsas.</summary>
public sealed record PlatformServices(
    IWindowsServices Services,
    IPrintJobReader PrintJobs,
    ISpoolDirectory SpoolDirectory,
    IProcessLauncher Processes,
    Func<DateTime> Now)
{
    public static PlatformServices Windows { get; } = new(
        new WindowsServices(),
        new WmiPrintJobReader(),
        new WindowsSpoolDirectory(),
        new WindowsProcessLauncher(),
        () => DateTime.Now);
}
