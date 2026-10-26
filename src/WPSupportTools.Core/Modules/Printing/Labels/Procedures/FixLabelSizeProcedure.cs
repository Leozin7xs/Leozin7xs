using WPSupportTools.Core.Platform;
using WPSupportTools.Core.Procedures;

namespace WPSupportTools.Core.Modules.Printing.Labels.Procedures;

/// <summary>
/// Volta tamanho e orientação para o padrão nas preferências de impressão do usuário
/// (mesma tela "Preferências de impressão"). Altera somente esses campos e confere em seguida.
/// </summary>
public sealed class FixLabelSizeProcedure(PlatformServices platform) : IProcedure
{
    public string Id => "labels.fix-size";
    public string DisplayName => "Corrigindo tamanho da etiqueta";
    public bool RequiresElevation => false;

    public Task<ProcedureResult> ExecuteAsync(ProcedureContext context, CancellationToken cancellationToken)
    {
        var printer = context.State.GetText(LabelState.PrinterName)
                      ?? throw new InvalidOperationException("Etiquetadora não localizada.");

        var before = new DevMode(platform.Preferences.Read(printer));
        var desired = before.WithPaper(LabelConstants.StandardWidth, LabelConstants.StandardLength, DevMode.OrientationPortrait);
        platform.Preferences.Write(printer, desired.ToArray());

        var after = new DevMode(platform.Preferences.Read(printer));
        context.Log($"Preferências de '{printer}': antes {before.Describe()}; depois {after.Describe()}.");

        return Task.FromResult(CheckLabelSizeProcedure.IsStandard(after)
            ? ProcedureResult.Ok($"Corrigido: {before.Describe()} → {after.Describe()}.")
            : ProcedureResult.Fail("O driver não aceitou o tamanho padrão.", $"Ficou: {after.Describe()}"));
    }
}
