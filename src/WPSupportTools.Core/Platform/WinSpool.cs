using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WPSupportTools.Core.Platform;

/// <summary>
/// Acesso às impressoras pela API oficial do Windows (winspool.drv):
/// preferências do usuário (DEVMODE) e envio RAW.
/// </summary>
internal sealed class WinSpool : IPrinterPreferences, IRawPrinter
{
    private const int DmOutBuffer = 2;
    private const int DmInBuffer = 8;

    public byte[] Read(string printerName)
    {
        using var printer = PrinterHandle.Open(printerName);
        var size = DocumentProperties(IntPtr.Zero, printer.Handle, printerName, IntPtr.Zero, IntPtr.Zero, 0);
        if (size <= 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Não foi possível ler as preferências de '{printerName}'.");

        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (DocumentProperties(IntPtr.Zero, printer.Handle, printerName, buffer, IntPtr.Zero, DmOutBuffer) < 0)
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"Não foi possível ler as preferências de '{printerName}'.");

            var bytes = new byte[size];
            Marshal.Copy(buffer, bytes, 0, size);
            return bytes;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public void Write(string printerName, byte[] devMode)
    {
        using var printer = PrinterHandle.Open(printerName);
        var size = DocumentProperties(IntPtr.Zero, printer.Handle, printerName, IntPtr.Zero, IntPtr.Zero, 0);
        if (size <= 0)
            throw new Win32Exception(Marshal.GetLastWin32Error());

        var input = Marshal.AllocHGlobal(Math.Max(size, devMode.Length));
        var output = Marshal.AllocHGlobal(size);
        var info = Marshal.AllocHGlobal(IntPtr.Size);
        try
        {
            Marshal.Copy(devMode, 0, input, devMode.Length);

            // O driver mescla e valida os campos alterados, como faz a tela "Preferências de impressão".
            if (DocumentProperties(IntPtr.Zero, printer.Handle, printerName, output, input, DmInBuffer | DmOutBuffer) < 0)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "O driver recusou as preferências.");

            // PRINTER_INFO_9: preferências do usuário atual (não exige administrador).
            Marshal.WriteIntPtr(info, output);
            if (!SetPrinter(printer.Handle, 9, info, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Não foi possível gravar as preferências.");
        }
        finally
        {
            Marshal.FreeHGlobal(info);
            Marshal.FreeHGlobal(output);
            Marshal.FreeHGlobal(input);
        }
    }

    public void Send(string printerName, string documentName, byte[] data)
    {
        using var printer = PrinterHandle.Open(printerName);
        var doc = new DocInfo1 { DocName = documentName, OutputFile = null, DataType = "RAW" };

        if (StartDocPrinter(printer.Handle, 1, ref doc) == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Não foi possível enviar para '{printerName}'.");
        try
        {
            if (!StartPagePrinter(printer.Handle))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            var buffer = Marshal.AllocHGlobal(data.Length);
            try
            {
                Marshal.Copy(data, 0, buffer, data.Length);
                if (!WritePrinter(printer.Handle, buffer, data.Length, out var written) || written != data.Length)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), $"Envio incompleto para '{printerName}'.");
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
                EndPagePrinter(printer.Handle);
            }
        }
        finally
        {
            EndDocPrinter(printer.Handle);
        }
    }

    private sealed class PrinterHandle : IDisposable
    {
        private PrinterHandle(IntPtr handle) => Handle = handle;

        public IntPtr Handle { get; }

        public static PrinterHandle Open(string printerName)
        {
            if (!OpenPrinter(printerName, out var handle, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"Impressora '{printerName}' não encontrada.");
            return new PrinterHandle(handle);
        }

        public void Dispose() => ClosePrinter(Handle);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DocInfo1
    {
        public string DocName;
        public string? OutputFile;
        public string DataType;
    }

    [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool OpenPrinter(string printerName, out IntPtr printer, IntPtr defaults);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr printer);

    [DllImport("winspool.drv", EntryPoint = "DocumentPropertiesW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int DocumentProperties(
        IntPtr hwnd, IntPtr printer, string deviceName, IntPtr devModeOutput, IntPtr devModeInput, int mode);

    [DllImport("winspool.drv", EntryPoint = "SetPrinterW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetPrinter(IntPtr printer, int level, IntPtr info, int command);

    [DllImport("winspool.drv", EntryPoint = "StartDocPrinterW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int StartDocPrinter(IntPtr printer, int level, ref DocInfo1 docInfo);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool EndDocPrinter(IntPtr printer);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool StartPagePrinter(IntPtr printer);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool EndPagePrinter(IntPtr printer);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool WritePrinter(IntPtr printer, IntPtr buffer, int count, out int written);
}
