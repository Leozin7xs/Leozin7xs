using Microsoft.Win32;

namespace WPSupportTools.Core.Platform;

internal sealed class WindowsSpoolDirectory : ISpoolDirectory
{
    public string StandardPath { get; } = Path.Combine(Environment.SystemDirectory, "spool", "PRINTERS");

    /// <remarks>Somente leitura do registro; nada é alterado.</remarks>
    public string? ConfiguredPath()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Print\Printers");
        var value = key?.GetValue("DefaultSpoolDirectory") as string;
        return string.IsNullOrWhiteSpace(value) ? null : Environment.ExpandEnvironmentVariables(value);
    }
}
