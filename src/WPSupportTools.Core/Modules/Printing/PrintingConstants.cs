namespace WPSupportTools.Core.Modules.Printing;

/// <summary>Nomes e limites usados pelo módulo de impressão. Alterar aqui muda o comportamento em todo o módulo.</summary>
public static class PrintingConstants
{
    /// <summary>Nome do serviço de impressão do Windows (o mesmo de "net stop spooler").</summary>
    public const string SpoolerServiceName = "Spooler";

    /// <summary>Tempo máximo para o serviço parar ou iniciar.</summary>
    public static readonly TimeSpan ServiceTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Nome do processo do TSPrint (TSPrint.exe).</summary>
    public const string TSPrintProcessName = "TSPrint";

    /// <summary>
    /// Caminho completo do TSPrint.exe nas unidades.
    /// AGUARDANDO CONFIRMAÇÃO: enquanto for null, a etapa "Abrindo o TSPrint" fica em modo de simulação.
    /// </summary>
    public static string? TSPrintExecutablePath => null;

    /// <summary>Tempo máximo esperando o TSPrint aparecer depois de aberto.</summary>
    public static readonly TimeSpan TSPrintStartTimeout = TimeSpan.FromSeconds(15);

    /// <summary>Espera após religar o spooler, para ele recarregar a fila antes de conferir.</summary>
    public static TimeSpan SpoolerSettleDelay { get; set; } = TimeSpan.FromSeconds(3);
}

/// <summary>Informações que as etapas de verificação deixam para as seguintes.</summary>
public static class PrintingState
{
    public static IReadOnlyList<string>? GetTargets(WPSupportTools.Core.Procedures.RunState state) =>
        state.GetText(TargetPrinters) is { Length: > 0 } text ? text.Split('\n') : null;

    public static void SetTargets(WPSupportTools.Core.Procedures.RunState state, IEnumerable<string> printers) =>
        state.SetText(TargetPrinters, string.Join('\n', printers));

    /// <summary>A fila está travada (ou o spooler parado): seguir pelo caminho "reiniciar spooler".</summary>
    public const string QueueNeedsReset = "printing.queue-needs-reset";

    /// <summary>
    /// Impressoras cuja fila será limpa (nomes separados por quebra de linha). Definido na verificação
    /// da fila e enviado ao processo de administrador, para ele mexer somente nessas filas.
    /// </summary>
    public const string TargetPrinters = "printing.target-printers";

    /// <summary>O TSPrint está aberto na sessão do usuário.</summary>
    public const string TSPrintRunning = "printing.tsprint-running";
}
