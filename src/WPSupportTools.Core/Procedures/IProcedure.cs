namespace WPSupportTools.Core.Procedures;

/// <summary>
/// Um procedimento de manutenção: uma ação única, explícita e previamente autorizada.
/// Cada procedimento vive em sua própria classe para que a sequência possa ser
/// reorganizada sem reescrever a aplicação.
/// </summary>
/// <remarks>
/// Regras para quem implementa um procedimento:
/// <list type="bullet">
///   <item>Executar somente a ação descrita em <see cref="DisplayName"/>; nada além.</item>
///   <item>Validar que o alvo existe e é o componente esperado antes de qualquer ação destrutiva.</item>
///   <item>Se o alvo não existir, retornar falha. Nunca procurar alternativas.</item>
///   <item>Nunca montar comandos a partir de texto vindo de fora da aplicação.</item>
///   <item>Respeitar o <see cref="CancellationToken"/> (usado para o tempo limite).</item>
/// </list>
/// </remarks>
public interface IProcedure
{
    /// <summary>Identificador estável, usado no log. Ex.: "printing.stop-spooler".</summary>
    string Id { get; }

    /// <summary>Texto mostrado ao usuário. Ex.: "Parando serviço de impressão".</summary>
    string DisplayName { get; }

    /// <summary>Se verdadeiro, o procedimento só roda em um processo elevado (administrador).</summary>
    bool RequiresElevation { get; }

    /// <summary>Tempo máximo de execução. Ao estourar, o procedimento é considerado falho.</summary>
    TimeSpan Timeout => TimeSpan.FromMinutes(2);

    /// <summary>
    /// Null quando a etapa deve rodar. Caso contrário, o motivo exibido como "não necessária".
    /// Avaliado antes de executar; ver <see cref="ConditionalProcedure"/>.
    /// </summary>
    string? WhyNotNeeded(RunState state) => null;

    Task<ProcedureResult> ExecuteAsync(ProcedureContext context, CancellationToken cancellationToken);
}
