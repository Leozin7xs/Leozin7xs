using System.ServiceProcess;
using WPSupportTools.App.Service;

namespace WPSupportTools.App;

/// <summary>
/// Ponto de entrada. O mesmo executável é:
/// <list type="bullet">
///   <item><c>--service</c>: o serviço do Windows (iniciado pelo próprio Windows);</item>
///   <item>qualquer outro caso: a interface WPF (que também trata --worker, --install e --uninstall).</item>
/// </list>
/// </summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args is ["--service"])
        {
            ServiceBase.Run(new SupportService());
            return 0;
        }

        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}
