using WPSupportTools.Core.Platform;
using WPSupportTools.Core.Procedures;

namespace WPSupportTools.Core.Modules.Printing.Labels.Procedures;

/// <summary>Calibra a etiquetadora (mesmo efeito do botão "Calibrate" do driver).</summary>
public sealed class CalibrateLabelPrinterProcedure(PlatformServices platform) : IProcedure
{
    public string Id => "labels.calibrate";
    public string DisplayName => "Calibrando a etiquetadora";
    public bool RequiresElevation => false;

    public Task<ProcedureResult> ExecuteAsync(ProcedureContext context, CancellationToken cancellationToken)
    {
        var printer = context.State.GetText(LabelState.PrinterName)
                      ?? throw new InvalidOperationException("Etiquetadora não localizada.");
        var language = Enum.Parse<ZebraLanguage>(context.State.GetText(LabelState.Language) ?? "");

        platform.Raw.Send(printer, "WP Support Tools - calibração", ZebraModel.CalibrationCommand(language));
        context.Log($"Comando de calibração ({language}) enviado para '{printer}'.");
        return Task.FromResult(ProcedureResult.Ok("Calibração enviada. A etiquetadora vai avançar algumas etiquetas."));
    }
}
