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
/// <param name="Input">Pergunta feita antes de executar (ex.: a licença do UniLab).</param>
public sealed record SupportAction(
    string Title,
    string Description,
    SupportActionKind Kind,
    string? SequenceId,
    string IconGlyph,
    IReadOnlyList<SupportAction>? Choices = null,
    string CompletionHint = "Tente imprimir novamente.",
    InputPrompt? Input = null);

/// <summary>
/// Pergunta feita antes de executar a sequência. A resposta, já validada e normalizada por
/// <see cref="Normalize"/>, vai para o <c>RunState</c> com a chave <see cref="StateKey"/>.
/// </summary>
/// <param name="Normalize">Devolve (valor normalizado, null) ou (null, mensagem de erro).</param>
public sealed record InputPrompt(
    string Question,
    string Help,
    string Example,
    string StateKey,
    Func<string, (string? Value, string? Error)> Normalize);

/// <summary>Uma área de suporte (Impressão, UniLab, Rede, Telefonia...).</summary>
public sealed record SupportModule(string Id, string DisplayName, IReadOnlyList<SupportAction> Actions);
