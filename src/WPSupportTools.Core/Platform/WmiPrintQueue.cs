using System.Management;

namespace WPSupportTools.Core.Platform;

/// <summary>Fila de impressão pelo WMI do Windows (Win32_PrintJob / Win32_Printer).</summary>
internal sealed class WmiPrintQueue : IPrintQueue
{
    private const uint ErrorAccessDenied = 5;

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

    public bool CancelAllJobs(string printerName)
    {
        // Busca exata pelo nome; aspas e barras são escapadas para a consulta WQL.
        // "SELECT *" é necessário: sem a chave (DeviceID) o objeto volta sem caminho e o
        // Windows recusa executar métodos nele (InvalidOperationException).
        var escaped = printerName.Replace("\\", "\\\\").Replace("'", "\\'");
        using var searcher = new ManagementObjectSearcher($"SELECT * FROM Win32_Printer WHERE Name = '{escaped}'");
        using var collection = searcher.Get();

        var printer = collection.Cast<ManagementObject>()
            .FirstOrDefault(p => string.Equals(p["Name"] as string, printerName, StringComparison.OrdinalIgnoreCase));
        if (printer is null)
            return false;

        using (printer)
        {
            var result = printer.InvokeMethod("CancelAllJobs", null);
            var code = result is null ? 0u : Convert.ToUInt32(result);
            if (code == ErrorAccessDenied)
                throw new UnauthorizedAccessException($"Acesso negado ao cancelar documentos de '{printerName}'.");
            if (code != 0)
                throw new InvalidOperationException($"O Windows recusou cancelar os documentos de '{printerName}' (código {code}).");
        }

        return true;
    }
}
