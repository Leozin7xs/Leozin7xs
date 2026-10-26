namespace WPSupportTools.Core.Platform;

/// <summary>Estados de um trabalho de impressão (mesmos valores de Win32_PrintJob.StatusMask).</summary>
[Flags]
public enum PrintJobStatus : uint
{
    None = 0,
    Paused = 1,
    Error = 2,
    Deleting = 4,
    Spooling = 8,
    Printing = 16,
    Offline = 32,
    PaperOut = 64,
    Printed = 128,
    Deleted = 256,
    Blocked = 512,
    UserInterventionRequired = 1024,
    Restarting = 2048,
    Complete = 4096,
    Retained = 8192,
}

/// <summary>Somente o necessário. Nome do documento e do usuário NÃO são lidos.</summary>
public sealed record PrintJobInfo(string PrinterName, PrintJobStatus Status, DateTime? SubmittedAt);

/// <summary>Fila de impressão do Windows, sempre através do próprio serviço de impressão.</summary>
public interface IPrintQueue
{
    /// <summary>Documentos em todas as filas (somente leitura).</summary>
    IReadOnlyList<PrintJobInfo> ReadJobs();

    /// <summary>
    /// Igual a "Cancelar todos os documentos" na janela da fila, só nesta impressora.
    /// Retorna false se a impressora não existir.
    /// </summary>
    bool CancelAllJobs(string printerName);
}
