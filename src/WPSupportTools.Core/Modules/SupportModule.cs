namespace WPSupportTools.Core.Modules;

public enum SupportActionKind
{
    Diagnostico,
    Correcao,
}

/// <summary>
/// Uma ação que o usuário pode disparar. Executa uma sequência (<see cref="SequenceId"/>) ou
/// mostra uma pergunta com opções (<see cref="Choices"/>), cada uma com sua sequência.
/// </summary>
/// <param name="IconGlyph">Caractere da fonte Segoe MDL2 Assets / Segoe Fluent Icons.</param>
/// <param name="CompletionHint">Orientação exibida ao final (ex.: "Tente imprimir novamente.").</param>
public sealed record SupportAction(
    string Title,
    string Description,
    SupportActionKind Kind,
    string? SequenceId,
    string IconGlyph,
    IReadOnlyList<SupportAction>? Choices = null,
    string CompletionHint = "Tente imprimir novamente.");

/// <summary>Uma área de suporte (Impressão, UniLab, Rede, Telefonia...).</summary>
public sealed record SupportModule(string Id, string DisplayName, IReadOnlyList<SupportAction> Actions);
