using System.ComponentModel;
using System.Diagnostics;

namespace WPSupportTools.Core.Platform;

internal sealed class WindowsProcessLauncher : IProcessLauncher
{
    public IReadOnlyList<RunningProcess> FindInCurrentSession(string processName)
    {
        var session = Process.GetCurrentProcess().SessionId;
        var result = new List<RunningProcess>();

        foreach (var process in Process.GetProcessesByName(processName))
        {
            using (process)
            {
                if (process.SessionId != session)
                    continue;

                string? product = null, version = null;
                try
                {
                    var info = process.MainModule?.FileVersionInfo;
                    product = info?.ProductName;
                    version = info?.FileVersion;
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
                {
                    // Sem acesso aos detalhes do executável; o nome do processo basta para a verificação.
                }

                result.Add(new RunningProcess(process.Id, product, version));
            }
        }

        return result;
    }

    public bool FileExists(string path) => File.Exists(path);

    public void Start(string executablePath)
    {
        using var _ = Process.Start(new ProcessStartInfo(executablePath)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(executablePath),
        });
    }
}
