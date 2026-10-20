using WPSupportTools.Core.Platform;
using WPSupportTools.Core.Procedures;

namespace WPSupportTools.Core.Modules.Printing.Correction.Procedures;

/// <summary>Equivalente a "net stop spooler". Não para outros serviços.</summary>
public sealed class StopPrintSpoolerProcedure(PlatformServices platform) : IProcedure
{
    public string Id => "printing.stop-spooler";
    public string DisplayName => "Parando serviço de impressão";
    public bool RequiresElevation => true;

    public Task<ProcedureResult> ExecuteAsync(ProcedureContext context, CancellationToken cancellationToken)
    {
        var spooler = platform.Services.Find(PrintingConstants.SpoolerServiceName);
        if (spooler is null)
            return Task.FromResult(ProcedureResult.Fail("Serviço de impressão (Spooler) não encontrado. Nada foi alterado."));

        if (spooler.State == ServiceState.Stopped)
            return Task.FromResult(ProcedureResult.Ok("O serviço de impressão já estava parado."));

        // "net stop spooler" pediria confirmação para parar os dependentes. Aqui, por segurança, não paramos.
        if (spooler.RunningDependents.Count > 0)
            return Task.FromResult(ProcedureResult.Fail(
                "Outros serviços dependem do serviço de impressão e estão em execução. Nada foi alterado.",
                "Dependentes em execução: " + string.Join(", ", spooler.RunningDependents)));

        try
        {
            platform.Services.Stop(spooler.Name, PrintingConstants.ServiceTimeout);
        }
        catch (TimeoutException)
        {
            return Task.FromResult(ProcedureResult.Fail(
                $"O serviço de impressão não parou em {PrintingConstants.ServiceTimeout.TotalSeconds:0} segundos."));
        }

        return Task.FromResult(ProcedureResult.Ok("Serviço de impressão parado."));
    }
}
