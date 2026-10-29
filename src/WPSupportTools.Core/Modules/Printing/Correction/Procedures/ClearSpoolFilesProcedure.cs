using WPSupportTools.Core.Platform;
using WPSupportTools.Core.Procedures;

namespace WPSupportTools.Core.Modules.Printing.Correction.Procedures;

/// <summary>
/// "Limpar o spooler", como o técnico faz à mão: com o serviço de impressão PARADO, apaga os
/// arquivos da fila (.SHD/.SPL) na pasta padrão do Windows.
/// <list type="bullet">
///   <item>Se a verificação identificou as impressoras travadas, apaga só os documentos delas
///         (identificados pelo nome da impressora dentro do .SHD). Documentos de outras impressoras ficam.</item>
///   <item>Se a fila estava travada a ponto de não dar para ler, apaga todos os documentos da pasta.</item>
///   <item>.SPL sem o .SHD correspondente (resto de documento) é apagado.</item>
///   <item>Só a pasta padrão, só se for a configurada no Windows; sem subpastas e sem seguir links.</item>
/// </list>
/// </summary>
public sealed class ClearSpoolFilesProcedure(PlatformServices platform) : IProcedure
{
    /// <summary>Logo após o spooler parar, outros processos podem segurar arquivos por alguns segundos.</summary>
    internal static TimeSpan LockedFileGracePeriod { get; set; } = TimeSpan.FromSeconds(5);

    public string Id => "printing.clear-spool-files";
    public string DisplayName => "Limpando arquivos da fila";
    public bool RequiresElevation => true;

    public async Task<ProcedureResult> ExecuteAsync(ProcedureContext context, CancellationToken cancellationToken)
    {
        var spooler = platform.Services.Find(PrintingConstants.SpoolerServiceName);
        if (spooler is null)
            return ProcedureResult.Fail("Serviço de impressão (Spooler) não encontrado. Nada foi apagado.");
        if (spooler.State != ServiceState.Stopped)
            return ProcedureResult.Fail("O serviço de impressão precisa estar parado para limpar a fila. Nada foi apagado.");

        var standard = Normalize(platform.SpoolFolder.StandardPath);
        var configured = platform.SpoolFolder.ConfiguredPath();
        if (configured is not null && !string.Equals(Normalize(configured), standard, StringComparison.OrdinalIgnoreCase))
            return ProcedureResult.Fail(
                "A pasta da fila de impressão deste computador não é a padrão do Windows. Nada foi apagado.",
                $"Configurada: {configured}");

        var folder = new DirectoryInfo(standard);
        if (!folder.Exists)
            return ProcedureResult.Fail("Pasta da fila de impressão não encontrada. Nada foi apagado.", standard);
        if (folder.Attributes.HasFlag(FileAttributes.ReparsePoint))
            return ProcedureResult.Fail("A pasta da fila de impressão é um link. Nada foi apagado.", standard);

        var targets = PrintingState.GetTargets(context.State);
        var printers = platform.SpoolFolder.InstalledPrinterNames();
        var files = folder.EnumerateFiles("*", SearchOption.TopDirectoryOnly)
            .Where(f => !f.Attributes.HasFlag(FileAttributes.ReparsePoint))
            .ToList();
        var shadows = files.Where(f => f.Extension.Equals(".SHD", StringComparison.OrdinalIgnoreCase)).ToList();
        var spools = files.Where(f => f.Extension.Equals(".SPL", StringComparison.OrdinalIgnoreCase)).ToList();

        var toDelete = new List<FileInfo>();
        int kept = 0, unidentified = 0;
        foreach (var shadow in shadows)
        {
            var owner = await ReadOwnerAsync(shadow, printers, cancellationToken);
            var delete = targets is null || (owner is not null && targets.Contains(owner, StringComparer.OrdinalIgnoreCase));
            if (owner is null)
                unidentified++;

            if (!delete)
            {
                kept++;
                continue;
            }

            toDelete.Add(shadow);
            toDelete.AddRange(spools.Where(s => SameJob(s, shadow)));
        }

        // .SPL sem .SHD: resto de documento, sem impressora; sempre apagado.
        toDelete.AddRange(spools.Where(s => !shadows.Any(h => SameJob(s, h))));

        var errors = await DeleteAsync(toDelete, cancellationToken);
        var deleted = toDelete.Count - errors.Count;
        context.Log($"Arquivos da fila ({standard}): {deleted} apagado(s), {kept} documento(s) de outras impressoras mantido(s), " +
                    $"{unidentified} sem impressora identificada, {errors.Count} com erro" +
                    $"{(targets is null ? "; fila ilegível: todos os documentos foram incluídos" : "")}.");

        if (errors.Count > 0)
            return ProcedureResult.Fail($"{errors.Count} arquivo(s) da fila não puderam ser apagados.",
                string.Join(" | ", errors.Take(5)));

        return ProcedureResult.Ok(deleted == 0 ? "Não havia arquivos para limpar." : $"Fila limpa ({deleted} arquivo(s) apagado(s)).");
    }

    private static bool SameJob(FileInfo a, FileInfo b) =>
        string.Equals(Path.GetFileNameWithoutExtension(a.Name), Path.GetFileNameWithoutExtension(b.Name),
            StringComparison.OrdinalIgnoreCase);

    private static async Task<string?> ReadOwnerAsync(FileInfo shadow, IReadOnlyList<string> printers, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + LockedFileGracePeriod;
        while (true)
        {
            try
            {
                return SpoolFileAttribution.PrinterOf(await File.ReadAllBytesAsync(shadow.FullName, ct), printers);
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(500, ct);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }

    /// <summary>Apaga os arquivos; os que estiverem presos são tentados de novo até o limite.</summary>
    private static async Task<List<string>> DeleteAsync(List<FileInfo> files, CancellationToken ct)
    {
        var pending = files.DistinctBy(f => f.FullName, StringComparer.OrdinalIgnoreCase).ToList();
        var errors = new Dictionary<string, string>();
        var deadline = DateTime.UtcNow + LockedFileGracePeriod;

        while (true)
        {
            errors.Clear();
            foreach (var file in pending.ToList())
            {
                try
                {
                    file.Refresh();
                    if (file.Exists)
                        file.Delete();
                    pending.Remove(file);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    errors[file.Name] = $"{file.Name}: {ex.GetType().Name}: {ex.Message}";
                }
            }

            if (pending.Count == 0 || DateTime.UtcNow >= deadline)
                return errors.Values.ToList();
            await Task.Delay(500, ct);
        }
    }

    private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
