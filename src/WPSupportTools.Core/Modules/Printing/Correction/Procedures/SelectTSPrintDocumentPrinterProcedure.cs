using WPSupportTools.Core.Platform;
using WPSupportTools.Core.Procedures;

namespace WPSupportTools.Core.Modules.Printing.Correction.Procedures;

/// <summary>
/// Confere se o TSPrint imprime os documentos A4 (pasta "root") numa impressora que está funcionando.
/// Se ela não existe ou está desligada/com erro e há exatamente uma outra funcionando, troca no TSPrint
/// (só o nome dessa pasta; fecha e reabre o TSPrint).
/// </summary>
public sealed class SelectTSPrintDocumentPrinterProcedure(PlatformServices platform, string? executablePath) : IProcedure
{
    /// <summary>Quanto esperar o Windows listar as impressoras (com o spooler travado ele pode não responder).</summary>
    internal static TimeSpan ListTimeout { get; set; } = TimeSpan.FromSeconds(20);

    public SelectTSPrintDocumentPrinterProcedure(PlatformServices platform)
        : this(platform, PrintingConstants.TSPrintExecutablePath)
    {
    }

    public string Id => "printing.tsprint-printer";
    public string DisplayName => "Conferindo a impressora A4 no TSPrint";
    public bool RequiresElevation => false;
    public TimeSpan Timeout => ListTimeout + TSPrintPrinterSwitch.MaxDuration;

    public async Task<ProcedureResult> ExecuteAsync(ProcedureContext context, CancellationToken cancellationToken)
    {
        string? config;
        try
        {
            config = platform.TSPrintConfig.ReadPrinterConfig();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ProcedureResult.Fail("Não foi possível ler a configuração do TSPrint.", ex.Message);
        }

        if (config is null)
            return ProcedureResult.NotNeeded("TSPrint não encontrado neste computador.");

        var current = TSPrintFolderPrinter.PrinterOf(config, PrintingConstants.TSPrintDocumentFolders);
        if (current is null)
            return ProcedureResult.NotNeeded("O TSPrint não tem impressora A4 (pasta \"root\") configurada.");

        IReadOnlyList<InstalledPrinter> printers;
        var listing = Task.Run(platform.Printers.List, CancellationToken.None);
        try
        {
            printers = await listing.WaitAsync(ListTimeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            _ = listing.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
            return ProcedureResult.Fail("O Windows não respondeu ao listar as impressoras.");
        }

        foreach (var p in printers.Where(DocumentPrinterSelector.IsPhysicalDocumentPrinter))
            context.Log($"Impressora: '{p.Name}' (driver {p.DriverName}, porta {p.PortName}, " +
                        $"{(p.IsOffline ? "desligada/sem comunicação" : p.Problem ?? "funcionando")}{(p.IsDefault ? ", padrão" : "")}).");
        context.Log($"TSPrint: documentos A4 vão para '{current}'.");

        var choice = DocumentPrinterSelector.Select(printers, current);
        if (choice.SwitchTo is null)
        {
            return choice.AlreadyWorking
                ? ProcedureResult.Ok(choice.Message)
                : ProcedureResult.Fail(choice.Message);
        }

        var updated = TSPrintFolderPrinter.WithPrinter(config, PrintingConstants.TSPrintDocumentFolders, choice.SwitchTo.Name);
        if (updated is null)
            return ProcedureResult.Fail("Não foi possível alterar a configuração do TSPrint (formato não reconhecido).");

        context.Log(choice.Message);
        return await new TSPrintPrinterSwitch(platform, executablePath)
            .ApplyAsync(context, updated, "os documentos A4", choice.SwitchTo.Name, cancellationToken);
    }
}
