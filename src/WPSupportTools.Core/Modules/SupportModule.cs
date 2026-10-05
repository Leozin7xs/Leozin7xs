namespace WPSupportTools.Core.Modules;

public enum SupportActionKind
{
    Diagnostico,
    Correcao,
}

/// <summary>Uma ação que o usuário pode disparar (ex.: "Corrigir problema de impressão").</summary>
/// <param name="IconGlyph">Caractere da fonte Segoe MDL2 Assets / Segoe Fluent Icons.</param>
public sealed record SupportAction(
    string Title,
    string Description,
    SupportActionKind Kind,
    string SequenceId,
    string IconGlyph);

/// <summary>Uma área de suporte (Impressão, UniLab, Rede, Telefonia...).</summary>
public sealed record SupportModule(string Id, string DisplayName, IReadOnlyList<SupportAction> Actions);
