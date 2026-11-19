using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WPSupportTools.Core.Platform;

/// <summary>
/// Conexões com impressoras compartilhadas por outro computador (\\servidor\impressora), do usuário atual.
/// Não exige administrador. Usado para "excluir e conectar de novo" quando a fila de uma impressora
/// compartilhada não limpa, como o técnico faz à mão.
/// </summary>
public interface IPrinterConnections
{
    /// <summary>
    /// O computador que compartilha responde e ainda compartilha esta impressora? Somente leitura.
    /// Deve ser conferido antes de remover a conexão, para a unidade não ficar sem impressora.
    /// </summary>
    bool IsShared(string server, string shareName, string connectionName);

    /// <summary>Remove a conexão (só do usuário atual; nada muda no computador que compartilha).</summary>
    void Remove(string connectionName);

    /// <summary>Cria a conexão (\\servidor\impressora).</summary>
    void Add(string connectionName);

    void SetDefault(string printerName);
}

internal sealed class WindowsPrinterConnections : IPrinterConnections
{
    /// <summary>Com o computador desligado, listar as impressoras dele pode demorar muito.</summary>
    private static readonly TimeSpan ServerTimeout = TimeSpan.FromSeconds(15);

    private const uint PrinterEnumName = 0x00000008;

    public bool IsShared(string server, string shareName, string connectionName)
    {
        var listing = Task.Run(() => SharedPrinters(server));
        if (!listing.Wait(ServerTimeout))
        {
            _ = listing.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
            return false;
        }

        var printerPart = connectionName[(connectionName.LastIndexOf('\\') + 1)..];
        return listing.Result.Any(p =>
            string.Equals(p.Share, shareName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(p.Printer, printerPart, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(p.Printer, connectionName, StringComparison.OrdinalIgnoreCase));
    }

    public void Remove(string connectionName)
    {
        if (!DeletePrinterConnection(connectionName))
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Não foi possível remover a conexão '{connectionName}'.");
    }

    public void Add(string connectionName)
    {
        if (!AddPrinterConnection(connectionName))
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Não foi possível conectar '{connectionName}'.");
    }

    public void SetDefault(string printerName)
    {
        if (!SetDefaultPrinter(printerName))
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Não foi possível definir '{printerName}' como padrão.");
    }

    /// <summary>Impressoras compartilhadas pelo computador (PRINTER_INFO_2: nome da impressora e do compartilhamento).</summary>
    private static List<(string Printer, string? Share)> SharedPrinters(string server)
    {
        var name = server.StartsWith(@"\\", StringComparison.Ordinal) ? server : @"\\" + server;
        EnumPrinters(PrinterEnumName, name, 2, IntPtr.Zero, 0, out var needed, out _);
        if (needed == 0)
            return [];

        var buffer = Marshal.AllocHGlobal((int)needed);
        try
        {
            if (!EnumPrinters(PrinterEnumName, name, 2, buffer, needed, out _, out var count))
                return [];

            var result = new List<(string, string?)>();
            var size = Marshal.SizeOf<PrinterInfo2>();
            for (var i = 0; i < count; i++)
            {
                var info = Marshal.PtrToStructure<PrinterInfo2>(buffer + i * size);
                result.Add((info.PrinterName ?? "", info.ShareName));
            }

            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PrinterInfo2
    {
        public string? ServerName;
        public string? PrinterName;
        public string? ShareName;
        public string? PortName;
        public string? DriverName;
        public string? Comment;
        public string? Location;
        public IntPtr DevMode;
        public string? SepFile;
        public string? PrintProcessor;
        public string? Datatype;
        public string? Parameters;
        public IntPtr SecurityDescriptor;
        public uint Attributes;
        public uint Priority;
        public uint DefaultPriority;
        public uint StartTime;
        public uint UntilTime;
        public uint Status;
        public uint Jobs;
        public uint AveragePpm;
    }

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool EnumPrinters(uint flags, string name, uint level, IntPtr buffer, uint size, out uint needed, out uint returned);

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool AddPrinterConnection(string name);

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool DeletePrinterConnection(string name);

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetDefaultPrinter(string name);
}
