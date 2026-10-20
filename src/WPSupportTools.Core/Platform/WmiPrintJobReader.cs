using System.Management;

namespace WPSupportTools.Core.Platform;

/// <summary>Lê a fila de impressão pelo WMI do Windows (somente leitura).</summary>
internal sealed class WmiPrintJobReader : IPrintJobReader
{
    public IReadOnlyList<PrintJobInfo> ReadJobs()
    {
        using var searcher = new ManagementObjectSearcher("SELECT Name, StatusMask, TimeSubmitted FROM Win32_PrintJob");
        using var collection = searcher.Get();

        var jobs = new List<PrintJobInfo>();
        foreach (var item in collection.Cast<ManagementObject>())
        {
            using (item)
            {
                // "Name" vem como "<impressora>, <número do trabalho>".
                var name = item["Name"] as string ?? "";
                var comma = name.LastIndexOf(',');
                var printer = comma > 0 ? name[..comma] : name;

                var status = item["StatusMask"] is uint mask ? (PrintJobStatus)mask : PrintJobStatus.None;

                DateTime? submitted = null;
                if (item["TimeSubmitted"] is string dmtf && !string.IsNullOrEmpty(dmtf))
                    submitted = ManagementDateTimeConverter.ToDateTime(dmtf);

                jobs.Add(new PrintJobInfo(printer, status, submitted));
            }
        }

        return jobs;
    }
}
