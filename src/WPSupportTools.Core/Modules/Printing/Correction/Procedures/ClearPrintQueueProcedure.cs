using WPSupportTools.Core.Platform;
using WPSupportTools.Core.Procedures;

namespace WPSupportTools.Core.Modules.Printing.Correction.Procedures;

/// <summary>
/// Limpa a fila apagando os arquivos .SPL e .SHD da pasta de spool do Windows.
/// Regras de segurança:
/// <list type="bullet">
///   <item>Só roda com o serviço de impressão PARADO.</item>
///   <item>Só usa a pasta padrão do Windows, e só se ela for a pasta configurada no sistema.</item>
///   <item>Não entra em subpastas, não segue atalhos/links, não apaga outros tipos de arquivo.</item>
///   <item>Se algo não bater, falha sem apagar nada. Nunca procura outras pastas.</item>
/// </list>
/// </summary>
public sealed class ClearPrintQueueProcedure(PlatformServices platform) : IProcedure
{
    public string Id => "printing.clear-queue";
    public string DisplayName => "Limpando fila de impressão";
    public bool RequiresElevation => true;

    /// <summary>
    /// Logo após o spooler parar, outros processos de impressão do Windows podem segurar os arquivos
    /// por alguns segundos. Os arquivos que falharem são tentados de novo até este limite.
    /// </summary>
    internal static TimeSpan LockedFileGracePeriod { get; set; } = TimeSpan.FromSeconds(5);

    public async Task<ProcedureResult> ExecuteAsync(ProcedureContext context, CancellationToken cancellationToken)
    {
        var spooler = platform.Services.Find(PrintingConstants.SpoolerServiceName);
        if (spooler is null)
            return ProcedureResult.Fail("Serviço de impressão (Spooler) não encontrado. Nada foi apagado.");
        if (spooler.State != ServiceState.Stopped)
            return ProcedureResult.Fail(
                "O serviço de impressão precisa estar parado para limpar a fila. Nada foi apagado.");

        var standard = Normalize(platform.SpoolDirectory.StandardPath);
        var configured = platform.SpoolDirectory.ConfiguredPath();
        if (configured is not null && !string.Equals(Normalize(configured), standard, StringComparison.OrdinalIgnoreCase))
            return ProcedureResult.Fail(
                "A pasta da fila de impressão deste computador não é a padrão do Windows. Nada foi apagado.",
                $"Configurada: {configured}");

        var folder = new DirectoryInfo(standard);
        if (!folder.Exists)
            return ProcedureResult.Fail("Pasta da fila de impressão não encontrada. Nada foi apagado.", standard);
        if (folder.Attributes.HasFlag(FileAttributes.ReparsePoint))
            return ProcedureResult.Fail("A pasta da fila de impressão é um link. Nada foi apagado.", standard);

        int deleted = 0, ignored = 0;
        var pending = new List<FileInfo>();

        foreach (var file in folder.EnumerateFiles("*", SearchOption.TopDirectoryOnly))
        {
            var isSpoolFile = PrintingConstants.SpoolFileExtensions.Contains(file.Extension, StringComparer.OrdinalIgnoreCase);
            if (!isSpoolFile || file.Attributes.HasFlag(FileAttributes.ReparsePoint))
                ignored++;
            else
                pending.Add(file);
        }

        // Sempre os mesmos arquivos, já validados acima; nada novo é procurado nas novas tentativas.
        var errors = new Dictionary<string, string>();
        var deadline = DateTime.UtcNow + LockedFileGracePeriod;
        while (true)
        {
            errors.Clear();
            foreach (var file in pending.ToList())
            {
                try
                {
                    file.Delete();
                    pending.Remove(file);
                    deleted++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    errors[file.Name] = $"{ex.GetType().Name}: {ex.Message}";
                }
            }

            if (pending.Count == 0 || DateTime.UtcNow >= deadline)
                break;
            await Task.Delay(500, cancellationToken);
        }

        context.Log($"Fila de impressão ({standard}): {deleted} arquivo(s) apagado(s), " +
                    $"{ignored} ignorado(s), {errors.Count} com erro.");

        if (errors.Count > 0)
            return ProcedureResult.Fail($"{errors.Count} arquivo(s) da fila não puderam ser apagados.",
                string.Join(" | ", errors.Take(5).Select(e => $"{e.Key}: {e.Value}")));

        return ProcedureResult.Ok(deleted == 0
            ? "A fila já estava vazia."
            : $"Fila limpa ({deleted} arquivo(s) removido(s)).");
    }

    private static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
