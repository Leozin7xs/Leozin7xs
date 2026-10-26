using WPSupportTools.Core.Platform;
using WPSupportTools.Core.Procedures;

namespace WPSupportTools.Core.Modules.Printing.Labels.Procedures;

/// <summary>Confere tamanho (50,8 × 25,4 mm) e orientação (retrato) nas preferências de impressão. Somente leitura.</summary>
public sealed class CheckLabelSizeProcedure(PlatformServices platform) : IProcedure
{
    public string Id => "labels.check-size";
    public string DisplayName => "Conferindo tamanho da etiqueta";
    public bool RequiresElevation => false;

    public Task<ProcedureResult> ExecuteAsync(ProcedureContext context, CancellationToken cancellationToken)
    {
        var printer = context.State.GetText(LabelState.PrinterName)
                      ?? throw new InvalidOperationException("Etiquetadora não localizada.");

        var devMode = new DevMode(platform.Preferences.Read(printer));
        context.Log($"Preferências de '{printer}': {devMode.Describe()}.");

        var ok = IsStandard(devMode);
        context.State.Set(LabelState.SizeNeedsFix, !ok);
        return Task.FromResult(ProcedureResult.Ok(ok
            ? "Tamanho no padrão (50,8 × 25,4 mm)."
            : $"Fora do padrão: {devMode.Describe()}. Será corrigido."));
    }

    internal static bool IsStandard(DevMode devMode) =>
        Math.Abs(devMode.PaperWidth - LabelConstants.StandardWidth) <= LabelConstants.Tolerance &&
        Math.Abs(devMode.PaperLength - LabelConstants.StandardLength) <= LabelConstants.Tolerance &&
        devMode.Orientation == DevMode.OrientationPortrait;
}
