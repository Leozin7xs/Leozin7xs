using WPSupportTools.Core.Modules.Unilab;
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

    private const string PdfPrinter = "Microsoft Print to PDF";

    public string Id => "printing.tsprint-printer";
    public string DisplayName => "Conferindo a impressora A4 no TSPrint";
    public bool RequiresElevation => false;
    public TimeSpan Timeout => ListTimeout + TSPrintPrinterSwitch.MaxDuration;

    public async Task<ProcedureResult> ExecuteAsync(ProcedureContext context, CancellationToken cancellationToken)
    {
        var cloud = UnilabCloud.IsInstalled(platform);
        string? config;
        try
        {
            config = cloud ? platform.Unilab.ReadCloudConfig() : platform.TSPrintConfig.ReadPrinterConfig();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ProcedureResult.Fail("Não foi possível ler a configuração do TSPrint.", ex.Message);
        }

        if (cloud && UnilabCloud.LicenseOf(config) is null)
            return ProcedureResult.NotNeeded("O UniLab novo está sem licença (etapa anterior).");

        if (config is null)
        {
            // Sem arquivo de impressoras mas com o TSPrint configurado: começa uma lista vazia.
            if (platform.TSPrintConfig.ReadGeneralConfig() is null)
                return ProcedureResult.NotNeeded("TSPrint não encontrado neste computador.");
            config = "[]";
        }

        var current = cloud
            ? UnilabCloud.PrinterOf(config, PrintingConstants.TSPrintDocumentFolders)
            : TSPrintFolderPrinter.PrinterOf(config, PrintingConstants.TSPrintDocumentFolders);
        var hasRoot = !cloud && TSPrintFolderPrinter.HasFolder(config, PrintingConstants.TSPrintDocumentFolders);

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
        context.Log(current is null ? "TSPrint: sem impressora A4 configurada." : $"TSPrint: documentos A4 vão para '{current}'.");

        var choice = current is null
            ? DocumentPrinterSelector.SelectForEmptyTSPrint(printers)
            : DocumentPrinterSelector.Select(printers, current);
        if (choice.SwitchTo is null)
        {
            return choice.AlreadyWorking
                ? ProcedureResult.Ok(choice.Message)
                : ProcedureResult.Fail(choice.Message);
        }

        if (cloud)
        {
            List<UnilabCloud.PrinterRule> rules = [new("root", choice.SwitchTo.Name, Print: true)];
            if (UnilabCloud.PrinterOf(config, ["pdf"]) is null &&
                printers.FirstOrDefault(p => p.Name.Equals(PdfPrinter, StringComparison.OrdinalIgnoreCase)) is { } pdfPrinter)
                rules.Add(new("pdf", pdfPrinter.Name, Print: false));
            if (UnilabCloud.Apply(config, null, null, rules) is not { } cloudResult)
                return ProcedureResult.Fail("Não foi possível alterar a configuração do UniLab (formato não reconhecido).");

            context.Log(choice.Message);
            return await TSPrintPrinterSwitch.ForUnilabCloud(platform).ApplyAsync(
                context, () => platform.Unilab.WriteCloudConfig(cloudResult.Json),
                $"TSPrint do UniLab: os documentos A4 agora vão para {choice.SwitchTo.Name}", cancellationToken);
        }

        var updated = hasRoot
            ? TSPrintFolderPrinter.WithPrinter(config, PrintingConstants.TSPrintDocumentFolders, choice.SwitchTo.Name)
            : TSPrintFolderPrinter.WithFolderAdded(config, PrintingConstants.TSPrintDocumentFolders[0], choice.SwitchTo.Name);

        // Pasta "pdf" do padrão WP (baixa, mas não imprime), se faltar e o Windows tiver o "Microsoft Print to PDF".
        if (updated is not null && !TSPrintFolderPrinter.HasFolder(updated, ["pdf"]) &&
            printers.FirstOrDefault(p => p.Name.Equals(PdfPrinter, StringComparison.OrdinalIgnoreCase)) is { } pdf)
        {
            updated = TSPrintFolderPrinter.WithFolderAdded(updated, "pdf", pdf.Name, print: false) ?? updated;
        }

        if (updated is null)
            return ProcedureResult.Fail("Não foi possível alterar a configuração do TSPrint (formato não reconhecido).");

        context.Log(choice.Message);
        return await new TSPrintPrinterSwitch(platform, executablePath)
            .ApplyAsync(context, updated, "os documentos A4", choice.SwitchTo.Name, cancellationToken);
    }
}
