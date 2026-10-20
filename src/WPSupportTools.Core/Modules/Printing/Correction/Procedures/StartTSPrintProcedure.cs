using WPSupportTools.Core.Platform;
using WPSupportTools.Core.Procedures;

namespace WPSupportTools.Core.Modules.Printing.Correction.Procedures;

/// <summary>
/// Abre o TSPrint como o usuário atual (sem administrador), a partir do caminho fixo
/// definido em <see cref="PrintingConstants.TSPrintExecutablePath"/>. Não procura em outras pastas.
/// </summary>
public sealed class StartTSPrintProcedure(PlatformServices platform, string? executablePath) : IProcedure
{
    public StartTSPrintProcedure(PlatformServices platform)
        : this(platform, PrintingConstants.TSPrintExecutablePath)
    {
    }

    public string Id => "printing.start-tsprint";
    public string DisplayName => "Abrindo o TSPrint";
    public bool RequiresElevation => false;
    public TimeSpan Timeout => PrintingConstants.TSPrintStartTimeout + TimeSpan.FromSeconds(10);

    public async Task<ProcedureResult> ExecuteAsync(ProcedureContext context, CancellationToken cancellationToken)
    {
        if (executablePath is null)
        {
            context.Log("TSPrint: caminho do executável ainda não configurado; nenhuma ação executada.");
            return ProcedureResult.NotConfigured("Caminho do TSPrint ainda não configurado. Abra o TSPrint manualmente.");
        }

        if (!platform.Processes.FileExists(executablePath))
            return ProcedureResult.Fail("TSPrint não encontrado no local esperado. Nada foi aberto.", executablePath);

        platform.Processes.Start(executablePath);

        var deadline = platform.Now() + PrintingConstants.TSPrintStartTimeout;
        while (platform.Now() < deadline)
        {
            if (platform.Processes.FindInCurrentSession(PrintingConstants.TSPrintProcessName).Count > 0)
                return ProcedureResult.Ok("TSPrint aberto.");

            await Task.Delay(500, cancellationToken);
        }

        return ProcedureResult.Fail("O TSPrint foi aberto, mas não apareceu em execução.");
    }
}
