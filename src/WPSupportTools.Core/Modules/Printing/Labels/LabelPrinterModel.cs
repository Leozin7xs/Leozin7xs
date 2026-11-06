using System.Text.Json;
using System.Text.Json.Serialization;
using WPSupportTools.Core.Platform;

namespace WPSupportTools.Core.Modules.Printing.Labels;

/// <summary>
/// "Configuração modelo" de uma etiquetadora: as preferências completas (DEVMODE, incluindo o bloco
/// interno do driver com escuridão, modo, offsets e gráficos) de uma Zebra deixada exatamente no padrão WP
/// por um técnico. Só vale para o mesmo driver na mesma versão.
/// </summary>
public sealed record LabelPrinterModel(
    [property: JsonPropertyName("formato")] int Format,
    [property: JsonPropertyName("driver")] string Driver,
    [property: JsonPropertyName("versao_driver")] string? DriverVersion,
    [property: JsonPropertyName("impressora")] string Printer,
    [property: JsonPropertyName("computador")] string Computer,
    [property: JsonPropertyName("capturado_em")] DateTimeOffset CapturedAt,
    [property: JsonPropertyName("app")] string AppVersion,
    [property: JsonPropertyName("resumo")] string Summary,
    [property: JsonPropertyName("preferencias_usuario")] string UserDevMode,
    [property: JsonPropertyName("padrao_impressora")] string? PrinterDefaultDevMode)
{
    public const int CurrentFormat = 1;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static LabelPrinterModel? FromJson(string json) => JsonSerializer.Deserialize<LabelPrinterModel>(json, Json);

    /// <summary>Nome de arquivo sem caracteres inválidos: driver + versão.</summary>
    public string FileName()
    {
        var name = $"{Driver} {DriverVersion ?? "versao-desconhecida"}";
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name + ".wpst-modelo.json";
    }
}

/// <summary>Captura a configuração modelo de todas as Zebras instaladas neste computador.</summary>
public static class LabelPrinterModelCapture
{
    public sealed record Failure(string Printer, string Reason);

    public static (IReadOnlyList<LabelPrinterModel> Models, IReadOnlyList<Failure> Failures) Capture(
        IPrinterCatalog catalog,
        IPrinterPreferences preferences,
        Func<string, string?> driverVersion,
        Func<string, byte[]?> printerDefault,
        DateTimeOffset now,
        string computer,
        string appVersion)
    {
        var models = new List<LabelPrinterModel>();
        var failures = new List<Failure>();

        foreach (var printer in catalog.List().Where(p => ZebraModel.LanguageOf(p.DriverName) is not null))
        {
            try
            {
                var user = preferences.Read(printer.Name);
                var devMode = new DevMode(user);
                byte[]? defaults = null;
                try
                {
                    defaults = printerDefault(printer.Name);
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
                {
                    // O padrão da impressora é complementar: sem ele a captura ainda vale.
                }

                models.Add(new LabelPrinterModel(
                    LabelPrinterModel.CurrentFormat,
                    printer.DriverName,
                    SafeVersion(driverVersion, printer.Name),
                    printer.Name,
                    computer,
                    now,
                    appVersion,
                    $"{devMode.Describe()}; bloco interno do driver: {devMode.DriverExtraSize} bytes" +
                    (printer.IsOffline ? "; impressora offline na captura" : ""),
                    Convert.ToBase64String(user),
                    defaults is null ? null : Convert.ToBase64String(defaults)));
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or ArgumentException or InvalidOperationException)
            {
                failures.Add(new Failure(printer.Name, ex.Message));
            }
        }

        return (models, failures);
    }

    private static string? SafeVersion(Func<string, string?> driverVersion, string printer)
    {
        try
        {
            return driverVersion(printer);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Captura usando o Windows deste computador.</summary>
    public static (IReadOnlyList<LabelPrinterModel> Models, IReadOnlyList<Failure> Failures) CaptureWindows(string appVersion)
    {
        var spool = new WinSpool();
        return Capture(PlatformServices.Windows.Printers, spool, spool.DriverVersion, spool.ReadPrinterDefault,
            DateTimeOffset.Now, Environment.MachineName, appVersion);
    }
}
