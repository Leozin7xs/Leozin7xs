using Microsoft.Win32;

namespace WPSupportTools.Core.Platform;

/// <remarks>Somente leitura do registro; nada é alterado.</remarks>
internal sealed class WindowsSpoolFolder : ISpoolFolder
{
    private const string PrintersKey = @"SYSTEM\CurrentControlSet\Control\Print\Printers";

    public string StandardPath { get; } = Path.Combine(Environment.SystemDirectory, "spool", "PRINTERS");

    public string? ConfiguredPath()
    {
        using var key = Registry.LocalMachine.OpenSubKey(PrintersKey);
        var value = key?.GetValue("DefaultSpoolDirectory") as string;
        return string.IsNullOrWhiteSpace(value) ? null : Environment.ExpandEnvironmentVariables(value);
    }

    public IReadOnlyList<string> InstalledPrinterNames()
    {
        using var key = Registry.LocalMachine.OpenSubKey(PrintersKey);
        return key?.GetSubKeyNames() ?? [];
    }
}
