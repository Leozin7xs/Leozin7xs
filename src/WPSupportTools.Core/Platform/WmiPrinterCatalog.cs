using System.Management;
using Microsoft.Win32;

namespace WPSupportTools.Core.Platform;

internal sealed class WmiPrinterCatalog : IPrinterCatalog
{
    private const ushort StatusOffline = 7;
    private const ushort ErrorOffline = 9;

    /// <summary>Win32_Printer.DetectedErrorState. Avisos (pouco papel, pouco toner) não contam.</summary>
    internal static string? ProblemOf(ushort detectedErrorState) => detectedErrorState switch
    {
        4 => "sem papel",
        6 => "sem tinta/toner",
        7 => "com a tampa aberta",
        8 => "com papel atolado",
        10 => "pedindo manutenção",
        11 => "com a bandeja de saída cheia",
        _ => null,
    };

    public IReadOnlyList<InstalledPrinter> List()
    {
        using var searcher = new ManagementObjectSearcher(
            new ManagementScope(@"root\cimv2"),
            new ObjectQuery("SELECT Name, DriverName, PortName, WorkOffline, PrinterStatus, ExtendedPrinterStatus, DetectedErrorState, Default FROM Win32_Printer"),
            new System.Management.EnumerationOptions { Timeout = TimeSpan.FromSeconds(15), ReturnImmediately = true, Rewindable = false });
        using var collection = searcher.Get();

        var usbPorts = UsbPorts();
        var printers = new List<InstalledPrinter>();
        foreach (var item in collection.Cast<ManagementObject>())
        {
            using (item)
            {
                var error = item["DetectedErrorState"] is ushort d ? d : (ushort)0;
                var offline = item["WorkOffline"] is true ||
                              item["PrinterStatus"] is ushort s && s == StatusOffline ||
                              item["ExtendedPrinterStatus"] is ushort e && e == StatusOffline ||
                              error == ErrorOffline;

                printers.Add(new InstalledPrinter(
                    item["Name"] as string ?? "",
                    item["DriverName"] as string ?? "",
                    item["PortName"] as string ?? "",
                    offline,
                    item["Default"] is true,
                    ProblemOf(error),
                    usbPorts.TryGetValue(item["PortName"] as string ?? "", out var linked) ? linked : null));
            }
        }

        return printers;
    }

    /// <summary>GUID_DEVINTERFACE_USBPRINT: uma entrada por porta USB de impressora que já existiu neste PC.</summary>
    private const string UsbPrintInterfaces =
        @"SYSTEM\CurrentControlSet\Control\DeviceClasses\{28d78fad-5a12-11d1-ae5b-0000f803a8c2}";

    /// <summary>
    /// Portas USB de impressora (USB001...) → aparelho conectado agora? ("Control\Linked" = 1).
    /// Uma impressora trocada deixa a porta antiga sem aparelho. Falha na leitura: vazio (sem como saber).
    /// </summary>
    internal static Dictionary<string, bool> UsbPorts()
    {
        var ports = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var interfaces = Registry.LocalMachine.OpenSubKey(UsbPrintInterfaces);
            if (interfaces is null)
                return ports;
            foreach (var name in interfaces.GetSubKeyNames())
            {
                using var parameters = interfaces.OpenSubKey($@"{name}\#\Device Parameters");
                using var control = interfaces.OpenSubKey($@"{name}\#\Control");
                if (parameters?.GetValue("Base Name") is not string baseName || parameters.GetValue("Port Number") is not int number)
                    continue;
                var port = $"{baseName}{number:000}";
                var linked = control?.GetValue("Linked") is int l && l != 0;
                ports[port] = (ports.TryGetValue(port, out var already) && already) || linked;
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            ports.Clear();
        }
        return ports;
    }
}
