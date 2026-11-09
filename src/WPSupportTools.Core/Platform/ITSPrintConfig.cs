using System.Text;
using WPSupportTools.Core.Modules.Printing;

namespace WPSupportTools.Core.Platform;

/// <summary>
/// Configuração do TSPrint (UniLab) neste computador:
/// <list type="bullet">
///   <item>arquivo de impressoras (<see cref="PrintingConstants.TSPrintPrinterConfigPath"/>);</item>
///   <item>arquivo geral (<see cref="PrintingConstants.TSPrintGeneralConfigPath"/>): servidor, usuário RDP e
///         senha (token). O token nunca vai para o log;</item>
///   <item>usuário do Unilab (<c>Usuario=</c> no .ini/.txt da pasta Unilab do disco C:).</item>
/// </list>
/// </summary>
public interface ITSPrintConfig
{
    /// <summary>Conteúdo do arquivo de impressoras, ou null se ele não existir neste computador.</summary>
    string? ReadPrinterConfig();

    /// <summary>Grava o arquivo de impressoras, guardando antes uma cópia do atual.</summary>
    void WritePrinterConfig(string content);

    /// <summary>Conteúdo do arquivo geral (servidor), ou null se ele não existir.</summary>
    string? ReadGeneralConfig();

    /// <summary>Grava o arquivo geral, guardando antes uma cópia do atual.</summary>
    void WriteGeneralConfig(string content);

    /// <summary>Usuário do Unilab deste computador (sem o "@"), ou null se não encontrado.</summary>
    string? ReadUnilabUser();
}

public sealed class TSPrintConfigFile : ITSPrintConfig
{
    public string? ReadPrinterConfig() => Read(PrintingConstants.TSPrintPrinterConfigPath);

    public void WritePrinterConfig(string content) => Write(PrintingConstants.TSPrintPrinterConfigPath, content);

    public string? ReadGeneralConfig() => Read(PrintingConstants.TSPrintGeneralConfigPath);

    public void WriteGeneralConfig(string content) => Write(PrintingConstants.TSPrintGeneralConfigPath, content);

    public string? ReadUnilabUser()
    {
        // Pastas do disco C: com "unilab" no nome (Rede_Unilab, Unilab, Unilab_Sonda, Sonda_Unilab...).
        IEnumerable<string> folders;
        try
        {
            folders = Directory.EnumerateDirectories(@"C:\")
                .Where(d => Path.GetFileName(d).Contains("unilab", StringComparison.OrdinalIgnoreCase))
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        foreach (var folder in folders)
        {
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(folder, "*.ini").Concat(Directory.EnumerateFiles(folder, "*.txt")).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files)
            {
                try
                {
                    if (new FileInfo(file).Length > 64 * 1024)
                        continue;
                    if (UnilabUser.From(File.ReadAllLines(file)) is { } user)
                        return user;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
        return null;
    }

    private static string? Read(string path) => File.Exists(path) ? File.ReadAllText(path) : null;

    private static void Write(string path, string content)
    {
        var hasBom = true;   // o TSPrint grava os arquivos com BOM

        if (File.Exists(path))
        {
            var original = File.ReadAllBytes(path);
            hasBom = original is [0xEF, 0xBB, 0xBF, ..];

            // Cópia do arquivo anterior, fora da pasta do TSPrint, para desfazer à mão se preciso.
            var backups = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WPSupportTools", "Backups");
            Directory.CreateDirectory(backups);
            File.WriteAllBytes(Path.Combine(backups, $"{Path.GetFileName(path)}.{DateTime.Now:yyyyMMdd-HHmmss}"), original);
        }

        // Grava ao lado e troca de uma vez: o TSPrint nunca vê o arquivo pela metade.
        var temp = path + ".wpst-novo";
        File.WriteAllText(temp, content.TrimStart('\uFEFF'), new UTF8Encoding(hasBom));
        File.Move(temp, path, overwrite: true);
    }
}

/// <summary>Lê o usuário do Unilab de um .ini/.txt: linha <c>Usuario=@01451cxxxx</c> → <c>01451cxxxx</c>.</summary>
public static class UnilabUser
{
    public static string? From(IEnumerable<string> lines)
    {
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            var equals = line.IndexOf('=');
            if (equals <= 0)
                continue;

            var key = line[..equals].Trim();
            if (!key.Equals("Usuario", StringComparison.OrdinalIgnoreCase) &&
                !key.Equals("Usuário", StringComparison.OrdinalIgnoreCase))
                continue;

            var user = line[(equals + 1)..].Replace("@", "").Trim();
            if (user.Length > 0)
                return user;
        }
        return null;
    }
}
