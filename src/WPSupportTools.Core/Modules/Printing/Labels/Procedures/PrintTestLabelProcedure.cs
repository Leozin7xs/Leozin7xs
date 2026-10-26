using WPSupportTools.Core.Platform;
using WPSupportTools.Core.Procedures;

namespace WPSupportTools.Core.Modules.Printing.Labels.Procedures;

/// <summary>
/// Imprime uma etiqueta de teste enviada direto à impressora. Não usa a página de teste do Windows,
/// que sai em branco com as configurações padrão da WP (Dithering None / Clipart).
/// </summary>
public sealed class PrintTestLabelProcedure(PlatformServices platform) : IProcedure
{
    public string Id => "labels.test-label";
    public string DisplayName => "Imprimindo etiqueta de teste";
    public bool RequiresElevation => false;

    public Task<ProcedureResult> ExecuteAsync(ProcedureContext context, CancellationToken cancellationToken)
    {
        var printer = context.State.GetText(LabelState.PrinterName)
                      ?? throw new InvalidOperationException("Etiquetadora não localizada.");
        var language = Enum.Parse<ZebraLanguage>(context.State.GetText(LabelState.Language) ?? "");

        platform.Raw.Send(printer, "WP Support Tools - etiqueta de teste", ZebraModel.TestLabel(language));
        context.Log($"Etiqueta de teste ({language}) enviada para '{printer}'.");
        return Task.FromResult(ProcedureResult.Ok("Etiqueta de teste enviada."));
    }
}
