using System.Diagnostics;
using Microsoft.Win32;

namespace WPSupportTools.Core.Platform;

/// <summary>Dados internos de um executável (aba "Detalhes" das propriedades do arquivo).</summary>
public sealed record ExecutableInfo(long Length, string? Company, string? Product, string? ProductVersion);

/// <summary>Download e instalação do UniLab (site da Uniware) neste computador.</summary>
public interface IUnilabSetup
{
    bool Is64BitWindows { get; }

    Task<string> GetPageAsync(Uri page, CancellationToken cancellationToken);

    /// <summary>Tamanho anunciado pelo servidor (Content-Length), ou null.</summary>
    Task<long?> GetSizeAsync(Uri file, CancellationToken cancellationToken);

    /// <summary>Baixa para <paramref name="destination"/>; <paramref name="progress"/> recebe os bytes já baixados.</summary>
    Task DownloadAsync(Uri file, string destination, Action<long> progress, CancellationToken cancellationToken);

    ExecutableInfo Inspect(string path);

    /// <summary>Versão do UniLab instalada (Programas e Recursos), ou null.</summary>
    string? InstalledVersion();

    /// <summary>Roda o instalador e espera terminar; devolve o código de saída, ou null se passou do tempo.</summary>
    Task<int?> RunInstallerAsync(string path, string arguments, TimeSpan timeout, CancellationToken cancellationToken);

    /// <summary>Pasta nova e protegida para o download (só administrador grava nela).</summary>
    string CreatePrivateDownloadFolder();

    void DeleteFolder(string path);

    /// <summary>config.json do UniLab novo, ou null se ainda não existir.</summary>
    string? ReadCloudConfig();

    /// <summary>Grava o config.json do UniLab novo (com cópia do anterior).</summary>
    void WriteCloudConfig(string content);

    /// <summary>Tira o TSPrint antigo da inicialização do Windows (atalhos vão para Backups). Devolve os nomes.</summary>
    IReadOnlyList<string> DisableLegacyStartup();

    /// <summary>Tira da área de trabalho os atalhos do UniLab antigo (vão para Backups). Devolve os nomes.</summary>
    IReadOnlyList<string> RemoveLegacyUnilabShortcuts();
}

internal sealed class WindowsUnilabSetup : IUnilabSetup
{
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    public bool Is64BitWindows => Environment.Is64BitOperatingSystem;

    public async Task<string> GetPageAsync(Uri page, CancellationToken cancellationToken) =>
        await Http.GetStringAsync(page, cancellationToken);

    public async Task<long?> GetSizeAsync(Uri file, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Head, file);
        using var response = await Http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return response.Content.Headers.ContentLength;
    }

    public async Task DownloadAsync(Uri file, string destination, Action<long> progress, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        using var response = await Http.GetAsync(file, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);
        var buffer = new byte[1 << 16];
        long total = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            total += read;
            progress(total);
        }
    }

    public ExecutableInfo Inspect(string path)
    {
        var info = FileVersionInfo.GetVersionInfo(path);
        return new ExecutableInfo(new FileInfo(path).Length, info.CompanyName?.Trim(), info.ProductName?.Trim(), info.ProductVersion?.Trim());
    }

    public string? InstalledVersion()
    {
        foreach (var (hive, view) in new[]
                 {
                     (RegistryHive.CurrentUser, RegistryView.Default),
                     (RegistryHive.LocalMachine, RegistryView.Registry64),
                     (RegistryHive.LocalMachine, RegistryView.Registry32),
                 })
        {
            using var root = RegistryKey.OpenBaseKey(hive, view);
            using var uninstall = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
            if (uninstall is null)
                continue;

            foreach (var name in uninstall.GetSubKeyNames())
            {
                using var app = uninstall.OpenSubKey(name);
                if (Modules.Unilab.UnilabRelease.IsUnilabDisplayName(app?.GetValue("DisplayName") as string) &&
                    app?.GetValue("DisplayVersion") is string version && version.Length > 0)
                    return version.Trim();
            }
        }
        return null;
    }

    public async Task<int?> RunInstallerAsync(string path, string arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        // UseShellExecute: se o instalador pedir administrador, o Windows mostra o aviso normalmente.
        using var process = Process.Start(new ProcessStartInfo(path, arguments)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(path),
        }) ?? throw new InvalidOperationException("O instalador não abriu.");

        using var timer = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timer.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timer.Token);
            return process.ExitCode;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    public string CreatePrivateDownloadFolder()
    {
        // Pasta criada pelo processo de administrador em ProgramData: o usuário comum não altera o arquivo
        // entre a conferência e a instalação.
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "WPSupportTools", "Downloads", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    public void DeleteFolder(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Fica para a próxima: a pasta de downloads é do próprio app.
        }
    }

    public string? ReadCloudConfig() =>
        File.Exists(Modules.Unilab.UnilabCloud.ConfigPath) ? File.ReadAllText(Modules.Unilab.UnilabCloud.ConfigPath) : null;

    public void WriteCloudConfig(string content)
    {
        var path = Modules.Unilab.UnilabCloud.ConfigPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path))
            File.Copy(path, Path.Combine(BackupFolder(), $"unilab-config.json.{DateTime.Now:yyyyMMdd-HHmmss}"), overwrite: true);

        var temp = path + ".wpst-novo";
        File.WriteAllText(temp, content, new System.Text.UTF8Encoding(false));   // o UniLab grava sem BOM
        File.Move(temp, path, overwrite: true);
    }

    public IReadOnlyList<string> DisableLegacyStartup() =>
        MoveShortcuts(
            [Environment.GetFolderPath(Environment.SpecialFolder.Startup),
             Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup)],
            target => target.EndsWith(@"\TSPrint\TSPrint.exe", StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<string> RemoveLegacyUnilabShortcuts() =>
        MoveShortcuts(
            [Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
             Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)],
            target => string.Equals(Path.GetFullPath(target), Path.GetFullPath(Modules.Unilab.UnilabCloud.LegacyExecutable),
                StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<string> MoveShortcuts(IEnumerable<string> folders, Func<string, bool> matches)
    {
        var moved = new List<string>();
        foreach (var folder in folders.Where(Directory.Exists))
        {
            foreach (var link in Directory.EnumerateFiles(folder, "*.lnk"))
            {
                try
                {
                    if (ShortcutTarget(link) is not { } target || !matches(target))
                        continue;
                    File.Move(link, Path.Combine(BackupFolder(), $"{Path.GetFileName(link)}.{DateTime.Now:yyyyMMdd-HHmmss}"));
                    moved.Add(Path.GetFileName(link));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
                {
                    // Sem permissão (ex.: área de trabalho pública sem administrador): deixa como está.
                }
            }
        }
        return moved;
    }

    private static string? ShortcutTarget(string link)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null)
            return null;
        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            dynamic shortcut = shell.CreateShortcut(link);
            return (string?)shortcut.TargetPath is { Length: > 0 } target ? target : null;
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        }
    }

    private static string BackupFolder()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WPSupportTools", "Backups");
        Directory.CreateDirectory(folder);
        return folder;
    }
}
