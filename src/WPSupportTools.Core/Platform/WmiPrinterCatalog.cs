using System.Management;

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
                    ProblemOf(error)));
            }
        }

        return printers;
    }
}
