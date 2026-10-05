using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WPSupportTools.App.Elevation;

internal static class NativeMethods
{
    /// <summary>Código retornado quando o usuário clica em "Não" no aviso do UAC.</summary>
    public const int ErrorCancelled = 1223;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint clientProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint serverProcessId);
}
