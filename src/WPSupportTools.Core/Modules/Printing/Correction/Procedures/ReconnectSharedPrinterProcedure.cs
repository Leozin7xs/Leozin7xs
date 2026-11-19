using WPSupportTools.Core.Modules.Printing.Labels;
using WPSupportTools.Core.Modules.Printing.Labels.Procedures;
using WPSupportTools.Core.Platform;
using WPSupportTools.Core.Procedures;

namespace WPSupportTools.Core.Modules.Printing.Correction.Procedures;

/// <summary>
/// Impressora compartilhada por outro computador (ex.: "ZDesigner TLP 2844 em 192.168.0.107") cuja fila não
/// limpou mesmo depois de cancelar e reiniciar o serviço: como o técnico faz, exclui a conexão e conecta de novo
/// no mesmo computador e compartilhamento.
/// <list type="bullet">
///   <item>Só roda se ainda sobrou documento numa impressora tratada e ela é uma conexão compartilhada.</item>
///   <item>Antes de excluir, confere que o computador que compartilha responde e ainda compartilha a impressora;
///         se não, não mexe em nada (a unidade não fica sem impressora).</item>
///   <item>Só a conexão do usuário atual; nada muda no computador que compartilha. Sem administrador.</item>
///   <item>Se era a impressora padrão, volta a ser. Etiquetadora Zebra volta para o tamanho e as opções padrão
///         (as preferências do usuário somem junto com a conexão).</item>
/// </list>
/// A conferência seguinte diz se a fila ficou vazia.
/// </summary>
public sealed class ReconnectSharedPrinterProcedure(PlatformServices platform) : IProcedure
{
    /// <summary>Espera entre tentar conectar de novo e depois de reconectar (o Windows recarrega a fila).</summary>
    internal static TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(3);

    public string Id => "printing.reconnect-shared";
    public string DisplayName => "Reconectando impressora compartilhada";
    public bool RequiresElevation => false;
    public TimeSpan Timeout => TimeSpan.FromMinutes(2);

    public async Task<ProcedureResult> ExecuteAsync(ProcedureContext context, CancellationToken cancellationToken)
    {
        var targets = PrintingState.GetTargets(context.State);
        if (targets is null || platform.Connections is null)
            return ProcedureResult.NotNeeded("Não necessário.");

        IReadOnlyList<PrintJobInfo> jobs;
        try
        {
            jobs = platform.PrintQueue.ReadJobs();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            context.Log($"Não foi possível ler a fila antes de reconectar ({ex.GetType().Name}: {ex.Message}).");
            return ProcedureResult.NotNeeded("Não foi possível ler a fila; a conferência a seguir mostra o resultado.");
        }

        var remaining = targets.Where(t => jobs.Any(j => string.Equals(j.PrinterName, t, StringComparison.OrdinalIgnoreCase))).ToList();
        if (remaining.Count == 0)
            return ProcedureResult.NotNeeded("Não necessário: a fila já está limpa.");

        var installed = platform.Printers.List();
        var shared = remaining
            .Select(name => installed.FirstOrDefault(p => p.IsConnection && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
            .OfType<InstalledPrinter>()
            .ToList();
        if (shared.Count == 0)
            return ProcedureResult.NotNeeded("Não necessário: a impressora não é compartilhada por outro computador.");

        var done = new List<string>();
        foreach (var printer in shared)
        {
            var server = printer.ServerName!;
            var share = printer.ShareName ?? printer.Name[(printer.Name.LastIndexOf('\\') + 1)..];

            if (!platform.Connections.IsShared(server, share, printer.Name))
                return ProcedureResult.Fail(
                    $"A fila de '{printer.Name}' não limpou e o computador que compartilha a impressora ({server.TrimStart('\\')}) " +
                    "não respondeu. A impressora não foi removida. Confira se esse computador está ligado e tente de novo.");

            context.Log($"'{printer.Name}' é compartilhada por {server} (compartilhamento '{share}'); " +
                        "excluindo a conexão e conectando de novo.");
            try
            {
                platform.Connections.Remove(printer.Name);
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                return ProcedureResult.Fail($"A fila de '{printer.Name}' não limpou e o Windows não deixou excluir a conexão.",
                    ex.Message);
            }

            if (!await TryAddAsync(context, printer.Name, cancellationToken))
                return ProcedureResult.Fail(
                    $"A conexão com '{printer.Name}' foi removida e não foi possível conectar de novo.",
                    $"Conecte manualmente em {printer.Name}.");

            if (printer.IsDefault)
                TryRun(context, () => platform.Connections.SetDefault(printer.Name), "definir como padrão");

            if (ZebraModel.LanguageOf(printer.DriverName) is not null)
                TryRun(context, () =>
                {
                    var (before, after, ok) = FixLabelSizeProcedure.ApplyStandard(platform.Preferences, printer.Name);
                    context.Log($"Preferências de '{printer.Name}' após reconectar: antes {before}; depois {after}" +
                                $"{(ok ? "" : " (o driver não aceitou o padrão)")}.");
                }, "voltar as preferências da etiquetadora para o padrão");

            done.Add(printer.Name);
        }

        await Task.Delay(RetryDelay, cancellationToken);
        return ProcedureResult.Ok($"Reconectada: {string.Join(", ", done)}.");
    }

    private async Task<bool> TryAddAsync(ProcedureContext context, string name, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                platform.Connections!.Add(name);
                context.Log($"Conexão '{name}' criada de novo.");
                return true;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                context.Log($"Tentativa {attempt} de conectar '{name}' falhou: {ex.Message}");
                if (attempt == 1)
                    await Task.Delay(RetryDelay, cancellationToken);
            }
        }

        return false;
    }

    private static void TryRun(ProcedureContext context, Action action, string what)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException
                                       or KeyNotFoundException)
        {
            context.Log($"Não foi possível {what}: {ex.Message}");
        }
    }
}
