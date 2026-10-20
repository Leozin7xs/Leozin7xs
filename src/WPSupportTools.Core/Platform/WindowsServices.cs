using System.ComponentModel;
using System.ServiceProcess;

namespace WPSupportTools.Core.Platform;

/// <summary>Implementação com <see cref="ServiceController"/> (API oficial do Windows para serviços).</summary>
internal sealed class WindowsServices : IWindowsServices
{
    private const int ErrorAccessDenied = 5;

    public ServiceInfo? Find(string serviceName)
    {
        var services = ServiceController.GetServices();
        try
        {
            var service = services.FirstOrDefault(s =>
                string.Equals(s.ServiceName, serviceName, StringComparison.OrdinalIgnoreCase));
            if (service is null)
                return null;

            var dependents = service.DependentServices
                .Where(d => d.Status != ServiceControllerStatus.Stopped)
                .Select(d => d.ServiceName)
                .ToList();

            return new ServiceInfo(service.ServiceName, service.DisplayName, Map(service.Status),
                service.StartType == ServiceStartMode.Disabled, dependents);
        }
        finally
        {
            foreach (var s in services)
                s.Dispose();
        }
    }

    private static ServiceState Map(ServiceControllerStatus status) => status switch
    {
        ServiceControllerStatus.Stopped => ServiceState.Stopped,
        ServiceControllerStatus.StartPending => ServiceState.StartPending,
        ServiceControllerStatus.StopPending => ServiceState.StopPending,
        ServiceControllerStatus.Running => ServiceState.Running,
        ServiceControllerStatus.ContinuePending => ServiceState.ContinuePending,
        ServiceControllerStatus.PausePending => ServiceState.PausePending,
        _ => ServiceState.Paused,
    };

    public void Stop(string serviceName, TimeSpan timeout) =>
        Control(serviceName, timeout, ServiceControllerStatus.Stopped, s => s.Stop(stopDependentServices: false));

    public void Start(string serviceName, TimeSpan timeout) =>
        Control(serviceName, timeout, ServiceControllerStatus.Running, s => s.Start());

    private static void Control(
        string serviceName, TimeSpan timeout, ServiceControllerStatus target, Action<ServiceController> action)
    {
        using var service = new ServiceController(serviceName);
        try
        {
            action(service);
            service.WaitForStatus(target, timeout);
        }
        catch (InvalidOperationException ex) when (ex.InnerException is Win32Exception { NativeErrorCode: ErrorAccessDenied })
        {
            throw new UnauthorizedAccessException(ex.Message, ex);
        }
        catch (System.ServiceProcess.TimeoutException ex)
        {
            throw new System.TimeoutException(ex.Message, ex);
        }
    }
}
