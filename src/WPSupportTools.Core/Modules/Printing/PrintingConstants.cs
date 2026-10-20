namespace WPSupportTools.Core.Modules.Printing;

/// <summary>Nomes e limites usados pelo módulo de impressão. Alterar aqui muda o comportamento em todo o módulo.</summary>
public static class PrintingConstants
{
    /// <summary>Nome do serviço de impressão do Windows (o mesmo de "net stop spooler").</summary>
    public const string SpoolerServiceName = "Spooler";

    /// <summary>Tempo máximo para o serviço parar ou iniciar.</summary>
    public static readonly TimeSpan ServiceTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Um trabalho na fila há mais tempo que isso é considerado travado.</summary>
    public static readonly TimeSpan StuckJobAge = TimeSpan.FromMinutes(2);

    /// <summary>Tipos de arquivo da fila de impressão. Somente estes são apagados.</summary>
    public static readonly IReadOnlyList<string> SpoolFileExtensions = [".SPL", ".SHD"];

    /// <summary>Nome do processo do TSPrint (TSPrint.exe).</summary>
    public const string TSPrintProcessName = "TSPrint";

    /// <summary>
    /// Caminho completo do TSPrint.exe nas unidades.
    /// AGUARDANDO CONFIRMAÇÃO: enquanto for null, a etapa "Abrindo o TSPrint" fica em modo de simulação.
    /// </summary>
    public static string? TSPrintExecutablePath => null;

    /// <summary>Tempo máximo esperando o TSPrint aparecer depois de aberto.</summary>
    public static readonly TimeSpan TSPrintStartTimeout = TimeSpan.FromSeconds(15);
}

/// <summary>Informações que as etapas de verificação deixam para as seguintes.</summary>
public static class PrintingState
{
    /// <summary>A fila está travada (ou o spooler parado): seguir pelo caminho "reiniciar spooler".</summary>
    public const string QueueNeedsReset = "printing.queue-needs-reset";

    /// <summary>O TSPrint está aberto na sessão do usuário.</summary>
    public const string TSPrintRunning = "printing.tsprint-running";
}
