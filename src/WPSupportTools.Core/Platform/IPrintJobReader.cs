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
}

/// <summary>Somente o necessário. Nome do documento e do usuário NÃO são lidos.</summary>
public sealed record PrintJobInfo(string PrinterName, PrintJobStatus Status, DateTime? SubmittedAt);

public interface IPrintJobReader
{
    IReadOnlyList<PrintJobInfo> ReadJobs();
}
