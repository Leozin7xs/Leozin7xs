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

    /// <summary>Caminho completo do TSPrint.exe nas unidades (mesma pasta Rede_Unilab da configuração).</summary>
    public static string? TSPrintExecutablePath => @"C:\Rede_Unilab\TSPrint\TSPrint.exe";

    /// <summary>
    /// Arquivo de impressoras do TSPrint (UniLab): diz qual é a etiquetadora quando há mais de uma ligada
    /// e é onde o app troca a impressora da pasta de etiquetas. O arquivo geral (geral_cnf.data, com
    /// usuário e senha) nunca é lido.
    /// </summary>
    public const string TSPrintPrinterConfigPath = @"C:\Rede_Unilab\TSPrint\conf\print_doc_cnf.data";

    /// <summary>Nomes da pasta do TSPrint cujas impressões são etiquetas ("FOLDER" em print_doc_cnf.data).</summary>
    public static readonly IReadOnlyList<string> TSPrintLabelFolders = ["etiq", "etiqueta", "etiquetas"];

    /// <summary>Pasta do TSPrint dos documentos comuns (impressora A4) em print_doc_cnf.data.</summary>
    public static readonly IReadOnlyList<string> TSPrintDocumentFolders = ["root"];

    /// <summary>Tempo máximo esperando o TSPrint fechar antes de gravar a configuração.</summary>
    public static readonly TimeSpan TSPrintCloseTimeout = TimeSpan.FromSeconds(10);

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
