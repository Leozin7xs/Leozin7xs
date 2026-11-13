using WPSupportTools.Core.Platform;
using WPSupportTools.Core.Procedures;

namespace WPSupportTools.Core.Modules.Printing.Labels.Procedures;

/// <summary>
/// Confere tamanho (50,8 × 25,4 mm), orientação (retrato) e as opções de gráficos (Clipart, impressão direta)
/// nas preferências de impressão. Somente leitura.
/// </summary>
public sealed class CheckLabelSizeProcedure(PlatformServices platform) : IProcedure
{
    public string Id => "labels.check-size";
    public string DisplayName => "Conferindo tamanho e gráficos da etiqueta";
    public bool RequiresElevation => false;

    public Task<ProcedureResult> ExecuteAsync(ProcedureContext context, CancellationToken cancellationToken)
    {
        var printer = context.State.GetText(LabelState.PrinterName)
                      ?? throw new InvalidOperationException("Etiquetadora não localizada.");

        var data = platform.Preferences.Read(printer);
        context.Log($"Preferências de '{printer}': {Describe(data)}.");

        var ok = IsStandard(data);
        context.State.Set(LabelState.SizeNeedsFix, !ok);
        return Task.FromResult(ProcedureResult.Ok(ok
            ? "Tamanho (50,8 × 25,4 mm) e gráficos no padrão."
            : $"Fora do padrão: {Describe(data)}. Será corrigido."));
    }

    internal static bool IsStandard(byte[] devMode) => IsStandardSize(new DevMode(devMode)) && ZebraGraphics.IsStandard(devMode);

    internal static bool IsStandardSize(DevMode devMode) =>
        Math.Abs(devMode.PaperWidth - LabelConstants.StandardWidth) <= LabelConstants.Tolerance &&
        Math.Abs(devMode.PaperLength - LabelConstants.StandardLength) <= LabelConstants.Tolerance &&
        devMode.Orientation == DevMode.OrientationPortrait;

    internal static string Describe(byte[] devMode) => $"{new DevMode(devMode).Describe()}; {ZebraGraphics.Describe(devMode)}";
}
