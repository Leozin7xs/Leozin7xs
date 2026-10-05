using System.Globalization;
using System.Text;

namespace WPSupportTools.Core.Logging;

/// <summary>
/// Log local em texto simples, um arquivo por dia:
/// %LOCALAPPDATA%\WPSupportTools\Logs\wpst-AAAAMMDD.log
/// Nada é enviado para fora do computador.
/// </summary>
public sealed class FileAppLog : IAppLog
{
    private readonly object _gate = new();
    private readonly Func<DateTime> _now;

    public FileAppLog() : this(DefaultDirectory(), () => DateTime.Now)
    {
    }

    internal FileAppLog(string directory, Func<DateTime> now)
    {
        LogDirectory = directory;
        _now = now;
    }

    public string LogDirectory { get; }

    public static string DefaultDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WPSupportTools", "Logs");

    public void Write(LogLevel level, string message)
    {
        var now = _now();
        var line = $"[{now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}] " +
                   $"[{LevelLabel(level)}] {message.ReplaceLineEndings(" ")}{Environment.NewLine}";
        var path = Path.Combine(LogDirectory, $"wpst-{now.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}.log");

        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);
                File.AppendAllText(path, line, Encoding.UTF8);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Falha ao gravar o log nunca deve interromper o atendimento.
            }
        }
    }

    private static string LevelLabel(LogLevel level) => level switch
    {
        LogLevel.Info => "INFO ",
        LogLevel.Warning => "AVISO",
        LogLevel.Error => "ERRO ",
        _ => level.ToString().ToUpperInvariant(),
    };
}
