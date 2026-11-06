using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using WPSupportTools.Core;
using WPSupportTools.Core.Modules.Printing.Labels;

namespace WPSupportTools.App;

/// <summary>
/// "WPSupportTools.exe --capturar-modelo": o técnico deixa a Zebra exatamente no padrão WP e roda isto.
/// Somente leitura: grava, numa pasta da Área de Trabalho, um arquivo por Zebra com as preferências
/// completas do driver e, como reserva, a cópia de configurações do próprio Windows (printui /Ss).
/// </summary>
internal static class LabelModelCaptureCommand
{
    public static int Run()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "WPST-configuracao-modelo");

        try
        {
            var (models, failures) = LabelPrinterModelCapture.CaptureWindows(AppInfo.Version);
            if (models.Count == 0 && failures.Count == 0)
            {
                Show("Nenhuma etiquetadora Zebra (driver ZDesigner) instalada neste computador.", MessageBoxImage.Warning);
                return 1;
            }

            Directory.CreateDirectory(folder);
            var report = new StringBuilder();
            foreach (var model in models)
            {
                var file = Path.Combine(folder, model.FileName());
                File.WriteAllText(file, model.ToJson(), new UTF8Encoding(false));
                var windowsCopy = SaveWindowsCopy(model.Printer, Path.ChangeExtension(file, null) + ".printui.dat");

                report.AppendLine($"✔ {model.Printer}")
                      .AppendLine($"   driver {model.Driver} {model.DriverVersion ?? "(versão não lida)"}")
                      .AppendLine($"   {model.Summary}")
                      .AppendLine(windowsCopy ? "   cópia do Windows: salva" : "   cópia do Windows: não foi possível (o arquivo principal basta)")
                      .AppendLine();
            }
            foreach (var failure in failures)
                report.AppendLine($"✖ {failure.Printer}: {failure.Reason}").AppendLine();

            report.Append("Arquivos em: ").Append(folder);
            Show(report.ToString(), failures.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
            if (models.Count > 0)
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true })?.Dispose();
            return failures.Count == 0 ? 0 : 1;
        }
        catch (Exception ex)
        {
            Show($"Não foi possível capturar: {ex.Message}", MessageBoxImage.Error);
            return 2;
        }
    }

    /// <summary>printui /Ss: preferências do usuário (u), padrão da impressora (g), dados do driver (d) e PRINTER_INFO_2 (2).</summary>
    private static bool SaveWindowsCopy(string printer, string file)
    {
        try
        {
            if (File.Exists(file))
                File.Delete(file);

            using var process = Process.Start(new ProcessStartInfo(
                Path.Combine(Environment.SystemDirectory, "rundll32.exe"),
                $"printui.dll,PrintUIEntry /Ss /q /n \"{printer}\" /a \"{file}\" 2 d g u")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (process is null || !process.WaitForExit(TimeSpan.FromSeconds(60)))
                return false;
            return File.Exists(file) && new FileInfo(file).Length > 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static void Show(string message, MessageBoxImage icon) =>
        MessageBox.Show(message, $"{AppInfo.Name} — configuração modelo", MessageBoxButton.OK, icon);
}
