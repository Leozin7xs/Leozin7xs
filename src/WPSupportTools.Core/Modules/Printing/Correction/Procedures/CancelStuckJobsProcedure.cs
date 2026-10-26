using WPSupportTools.Core.Platform;
using WPSupportTools.Core.Procedures;

namespace WPSupportTools.Core.Modules.Printing.Correction.Procedures;

/// <summary>
/// Igual a abrir a fila e "Cancelar todos os documentos", somente nas impressoras que têm
/// documento travado (com erro). As filas das outras impressoras não são tocadas.
/// O cancelamento é feito pelo próprio serviço de impressão; nenhum arquivo é apagado à mão.
/// </summary>
public sealed class CancelStuckJobsProcedure(PlatformServices platform) : IProcedure
{
    public string Id => "printing.cancel-stuck-jobs";
    public string DisplayName => "Cancelando documentos travados";
    public bool RequiresElevation => true;

    public Task<ProcedureResult> ExecuteAsync(ProcedureContext context, CancellationToken cancellationToken)
    {
        var spooler = platform.Services.Find(PrintingConstants.SpoolerServiceName);
        if (spooler is null)
            return Task.FromResult(ProcedureResult.Fail("Serviço de impressão (Spooler) não encontrado. Nada foi alterado."));

        // Com o serviço parado não há fila para consultar; o reinício a seguir resolve esse caso.
        if (spooler.State != ServiceState.Running)
            return Task.FromResult(ProcedureResult.Ok("Serviço de impressão parado: não há fila para cancelar."));

        var printers = PrintQueueAnalyzer.PrintersWithStuckJobs(platform.PrintQueue.ReadJobs());
        if (printers.Count == 0)
            return Task.FromResult(ProcedureResult.Ok("Nenhum documento travado no momento."));

        var missing = new List<string>();
        foreach (var printer in printers)
        {
            if (platform.PrintQueue.CancelAllJobs(printer))
                context.Log($"Documentos cancelados na impressora '{printer}'.");
            else
                missing.Add(printer);
        }

        if (missing.Count > 0)
            return Task.FromResult(ProcedureResult.Fail(
                "Não foi possível encontrar a impressora para cancelar os documentos.", string.Join(", ", missing)));

        return Task.FromResult(ProcedureResult.Ok($"Documentos cancelados em: {string.Join(", ", printers)}."));
    }
}
