namespace WPSupportTools.Core.Platform;

public enum ServiceState
{
    Stopped,
    StartPending,
    StopPending,
    Running,
    ContinuePending,
    PausePending,
    Paused,
}

/// <param name="RunningDependents">Serviços que dependem deste e estão em execução.</param>
public sealed record ServiceInfo(
    string Name, string DisplayName, ServiceState State, bool IsDisabled, IReadOnlyList<string> RunningDependents);

/// <summary>Acesso a serviços do Windows. Opera somente pelo nome exato do serviço.</summary>
public interface IWindowsServices
{
    /// <summary>Retorna null se não existir um serviço com exatamente este nome.</summary>
    ServiceInfo? Find(string serviceName);

    /// <summary>Para o serviço (sem parar dependentes) e aguarda. Lança <see cref="TimeoutException"/>.</summary>
    void Stop(string serviceName, TimeSpan timeout);

    /// <summary>Inicia o serviço e aguarda. Lança <see cref="TimeoutException"/>.</summary>
    void Start(string serviceName, TimeSpan timeout);
}
