using WPSupportTools.Core.Platform;
using WPSupportTools.Core.Procedures;

namespace WPSupportTools.Core.Modules.Printing.Labels.Procedures;

/// <summary>
/// Volta tamanho, orientação e opções de gráficos (Clipart, impressão direta) para o padrão nas preferências de
/// impressão do usuário (mesma tela "Preferências de impressão"). Altera somente esses campos e confere em seguida.
/// </summary>
public sealed class FixLabelSizeProcedure(PlatformServices platform) : IProcedure
{
    public string Id => "labels.fix-size";
    public string DisplayName => "Corrigindo tamanho e gráficos da etiqueta";
    public bool RequiresElevation => false;

    public Task<ProcedureResult> ExecuteAsync(ProcedureContext context, CancellationToken cancellationToken)
    {
        var printer = context.State.GetText(LabelState.PrinterName)
                      ?? throw new InvalidOperationException("Etiquetadora não localizada.");

        var (before, after, ok) = ApplyStandard(platform.Preferences, printer);
        context.Log($"Preferências de '{printer}': antes {before}; depois {after}.");

        return Task.FromResult(ok
            ? ProcedureResult.Ok($"Corrigido: {before} → {after}.")
            : ProcedureResult.Fail("O driver não aceitou o padrão da etiqueta.", $"Ficou: {after}"));
    }

    /// <summary>Grava o padrão WP (tamanho, orientação e gráficos) e confere; não grava se já estiver no padrão.</summary>
    internal static (string Before, string After, bool Ok) ApplyStandard(IPrinterPreferences preferences, string printer)
    {
        var current = preferences.Read(printer);
        var before = CheckLabelSizeProcedure.Describe(current);
        if (CheckLabelSizeProcedure.IsStandard(current))
            return (before, before, true);

        // Só mexe no tamanho se ele estiver errado: o driver recalcula outros campos quando ele é regravado.
        var desired = CheckLabelSizeProcedure.IsStandardSize(new DevMode(current))
            ? current
            : new DevMode(current)
                .WithPaper(LabelConstants.StandardWidth, LabelConstants.StandardLength, DevMode.OrientationPortrait)
                .ToArray();
        preferences.Write(printer, ZebraGraphics.WithStandard(desired));

        var after = preferences.Read(printer);
        return (before, CheckLabelSizeProcedure.Describe(after), CheckLabelSizeProcedure.IsStandard(after));
    }
}
