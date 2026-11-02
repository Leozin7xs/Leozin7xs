using System.Diagnostics;
using System.IO;
using System.ServiceProcess;
using WPSupportTools.Core.Logging;
using WPSupportTools.Core.Service;

namespace WPSupportTools.App.Service;

/// <summary>
/// Instalação feita pelo técnico, uma vez por computador (pede a senha de administrador uma única vez):
/// copia o programa para Program Files, registra o serviço do Windows e cria os atalhos.
/// Usa só ferramentas do próprio Windows (sc.exe) com argumentos fixos.
/// </summary>
internal static class Installer
{
    private const string ShortcutName = "WP Support Tools.lnk";

    public static bool IsServiceInstalled() => QueryStatus() is not null;

    public static bool IsServiceRunning() => QueryStatus() == ServiceControllerStatus.Running;

    public static bool IsRunningFromInstallDirectory() =>
        string.Equals(Path.GetFullPath(Environment.ProcessPath ?? ""), Path.GetFullPath(ServiceConstants.InstalledExecutable),
            StringComparison.OrdinalIgnoreCase);

    public static void Install(IAppLog log)
    {
        var source = Environment.ProcessPath ?? throw new InvalidOperationException("Caminho do programa desconhecido.");
        var target = ServiceConstants.InstalledExecutable;

        StopService(log);
        Directory.CreateDirectory(ServiceConstants.InstallDirectory);

        if (!string.Equals(Path.GetFullPath(source), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
        {
            // Se uma cópia antiga estiver aberta, ela pode ser renomeada (não sobrescrita).
            if (File.Exists(target))
            {
                var old = target + ".old";
                if (File.Exists(old))
                    File.Delete(old);
                File.Move(target, old);
            }

            File.Copy(source, target);
            log.Write(LogLevel.Info, $"Programa copiado para {target}.");
        }

        var binPath = $"\"{target}\" --service";
        if (IsServiceInstalled())
            Sc("config", ServiceConstants.ServiceName, "binPath=", binPath, "start=", "auto");
        else
            Sc("create", ServiceConstants.ServiceName, "binPath=", binPath, "start=", "auto",
                "DisplayName=", ServiceConstants.DisplayName);

        Sc("description", ServiceConstants.ServiceName, ServiceConstants.Description);
        // Se o serviço parar inesperadamente (inclusive ao se atualizar), o Windows o reinicia.
        Sc("failure", ServiceConstants.ServiceName, "reset=", "86400", "actions=", "restart/5000/restart/5000/restart/60000");

        using (var service = new ServiceController(ServiceConstants.ServiceName))
        {
            service.Start();
            service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
        }

        CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), ShortcutName), target);
        CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), ShortcutName), target);
        log.Write(LogLevel.Info, "Serviço instalado e iniciado; atalhos criados.");
    }

    public static void Uninstall(IAppLog log)
    {
        StopService(log);
        if (IsServiceInstalled())
            Sc("delete", ServiceConstants.ServiceName);

        foreach (var shortcut in new[]
                 {
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), ShortcutName),
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), ShortcutName),
                 })
        {
            if (File.Exists(shortcut))
                File.Delete(shortcut);
        }

        log.Write(LogLevel.Info, "Serviço removido e atalhos apagados. A pasta do programa pode ser apagada manualmente.");
    }

    private static ServiceControllerStatus? QueryStatus()
    {
        try
        {
            using var service = new ServiceController(ServiceConstants.ServiceName);
            return service.Status;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static void StopService(IAppLog log)
    {
        var status = QueryStatus();
        if (status is null or ServiceControllerStatus.Stopped)
            return;

        using var service = new ServiceController(ServiceConstants.ServiceName);
        service.Stop();
        service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
        log.Write(LogLevel.Info, "Serviço parado para atualização da instalação.");
    }

    private static void Sc(params string[] args)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "sc.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in args)
            start.ArgumentList.Add(arg);

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Não foi possível executar sc.exe.");
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        if (!process.WaitForExit(30_000) || process.ExitCode != 0)
            throw new InvalidOperationException($"sc {args[0]} falhou: {output.Trim()}");
    }

    private static void CreateShortcut(string path, string target)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
                        ?? throw new InvalidOperationException("Windows Script Host indisponível.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            dynamic shortcut = shell.CreateShortcut(path);
            shortcut.TargetPath = target;
            shortcut.WorkingDirectory = Path.GetDirectoryName(target);
            shortcut.IconLocation = target + ",0";
            shortcut.Description = "Ferramentas de suporte técnico do Grupo WP";
            shortcut.Save();
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        }
    }
}
