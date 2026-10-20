namespace WPSupportTools.Core.Platform;

public sealed record RunningProcess(int Id, string? ProductName, string? FileVersion);

/// <summary>Consulta e abre programas na sessão do usuário atual.</summary>
public interface IProcessLauncher
{
    /// <summary>Processos com este nome (sem ".exe") na sessão do Windows do usuário atual.</summary>
    IReadOnlyList<RunningProcess> FindInCurrentSession(string processName);

    bool FileExists(string path);

    /// <summary>Abre o programa como o usuário atual (sem elevação).</summary>
    void Start(string executablePath);
}
