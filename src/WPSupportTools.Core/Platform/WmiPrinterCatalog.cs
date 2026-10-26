using System.Management;

namespace WPSupportTools.Core.Platform;

internal sealed class WmiPrinterCatalog : IPrinterCatalog
{
    private const ushort StatusOffline = 7;

    public IReadOnlyList<InstalledPrinter> List()
    {
        using var searcher = new ManagementObjectSearcher(
            "SELECT Name, DriverName, PortName, WorkOffline, PrinterStatus, ExtendedPrinterStatus, Default FROM Win32_Printer");
        using var collection = searcher.Get();

        var printers = new List<InstalledPrinter>();
        foreach (var item in collection.Cast<ManagementObject>())
        {
            using (item)
            {
                var offline = item["WorkOffline"] is true ||
                              item["PrinterStatus"] is ushort s && s == StatusOffline ||
                              item["ExtendedPrinterStatus"] is ushort e && e == StatusOffline;

                printers.Add(new InstalledPrinter(
                    item["Name"] as string ?? "",
                    item["DriverName"] as string ?? "",
                    item["PortName"] as string ?? "",
                    offline,
                    item["Default"] is true));
            }
        }

        return printers;
    }
}
