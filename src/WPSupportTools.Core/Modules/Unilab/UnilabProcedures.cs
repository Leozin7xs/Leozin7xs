using System.Text.RegularExpressions;
using WPSupportTools.Core.Modules.Printing;
using WPSupportTools.Core.Modules.Printing.Correction;
using WPSupportTools.Core.Modules.Printing.Labels;
using WPSupportTools.Core.Platform;
using WPSupportTools.Core.Procedures;

namespace WPSupportTools.Core.Modules.Unilab;

/// <summary>Informações que as etapas do UniLab deixam para as seguintes.</summary>
public static partial class UnilabState
{
    public const string License = "unilab.license";
    public const string InstallerFile = "unilab.installer-file";
    public const string Version = "unilab.version";
    public const string UpToDate = "unilab.up-to-date";

    [GeneratedRegex(@"^Unilab-\d+(\.\d+){1,3}-win(-x64)?\.exe$", RegexOptions.IgnoreCase)]
    private static partial Regex OfficialFileName();

    /// <summary>
    /// Instalador escolhido pela etapa anterior. O processo de administrador recebe só o nome do arquivo e a
    /// versão, e confere de novo que é um nome oficial (nada de caminho nem endereço vindo de fora).
    /// </summary>
    internal static UnilabInstaller? Installer(RunState state) =>
        state.GetText(InstallerFile) is { } file && OfficialFileName().IsMatch(file) &&
        System.Version.TryParse(state.GetText(Version), out var version)
            ? new UnilabInstaller(file, version, file.Contains("-x64", StringComparison.OrdinalIgnoreCase))
            : null;

    internal static string? OnlyIfUpdateNeeded(RunState state) =>
        state.IsSet(UpToDate) ? "Não necessário: o UniLab já está instalado na versão mais nova." : null;
}

/// <summary>Confere a licença digitada (ex.: @01451exemplo01).</summary>
public sealed class CheckUnilabLicenseProcedure : IProcedure
{
    public string Id => "unilab.license";
    public string DisplayName => "Conferindo a licença";
    public bool RequiresElevation => false;

    public Task<ProcedureResult> ExecuteAsync(ProcedureContext context, CancellationToken cancellationToken)
    {
        var (license, error) = UnilabCloud.NormalizeLicense(context.State.GetText(UnilabState.License));
        if (license is null)
            return Task.FromResult(ProcedureResult.Fail(error!));

        context.State.SetText(UnilabState.License, license);
        return Task.FromResult(ProcedureResult.Ok($"Licença {license} (código ADM {UnilabCloud.AdminCode(license)})."));
    }
}

/// <summary>Lê a página da Uniware e compara a versão mais nova com a instalada. Somente leitura.</summary>
public sealed class CheckUnilabVersionProcedure(PlatformServices platform) : IProcedure
{
    public string Id => "unilab.check";
    public string DisplayName => "Procurando a versão mais nova do UniLab";
    public bool RequiresElevation => false;
    public TimeSpan Timeout => TimeSpan.FromSeconds(45);

    public async Task<ProcedureResult> ExecuteAsync(ProcedureContext context, CancellationToken cancellationToken)
    {
        string html;
        try
        {
            html = await platform.Unilab.GetPageAsync(UnilabRelease.Page, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            return ProcedureResult.Fail("Não foi possível acessar o site da Uniware. Confira a internet e tente de novo.", ex.Message);
        }

        var installer = UnilabRelease.Choose(UnilabRelease.Parse(html), platform.Unilab.Is64BitWindows);
        if (installer is null)
            return ProcedureResult.Fail("O site da Uniware não tem instalador do UniLab para Windows agora. Chame o suporte.");

        var installed = platform.Unilab.InstalledVersion();
        context.Log($"UniLab: mais nova {installer.Version} ({installer.FileName}); instalada: {installed ?? "nenhuma"}.");

        context.State.SetText(UnilabState.InstallerFile, installer.FileName);
        context.State.SetText(UnilabState.Version, installer.Version.ToString());

        if (UnilabRelease.IsUpToDate(installed, installer.Version))
        {
            context.State.Set(UnilabState.UpToDate, true);
            return ProcedureResult.Ok($"O UniLab já está instalado na versão mais nova ({installed}).");
        }

        return ProcedureResult.Ok(installed is null
            ? $"UniLab {installer.Version} será instalado."
            : $"Versão nova: {installer.Version} (instalada: {installed}).");
    }
}

/// <summary>
/// Etapa de administrador: baixa o instalador para uma pasta protegida, confere (tamanho anunciado,
/// UNIWARE / Unilab / versão), instala em modo silencioso e confirma a versão. Apaga o instalador no fim.
/// </summary>
public sealed class InstallUnilabProcedure(PlatformServices platform) : IProcedure
{
    internal static TimeSpan InstallTimeout { get; set; } = TimeSpan.FromMinutes(10);

    public string Id => "unilab.install";
    public string DisplayName => "Baixando, conferindo e instalando o UniLab";
    public bool RequiresElevation => true;
    public TimeSpan Timeout => TimeSpan.FromMinutes(25) + InstallTimeout;

    public async Task<ProcedureResult> ExecuteAsync(ProcedureContext context, CancellationToken cancellationToken)
    {
        if (UnilabState.Installer(context.State) is not { } installer)
            return ProcedureResult.Fail("Versão do UniLab não definida (ou nome de instalador fora do padrão).");

        var folder = platform.Unilab.CreatePrivateDownloadFolder();
        var path = Path.Combine(folder, installer.FileName);
        try
        {
            long? size;
            try
            {
                size = await platform.Unilab.GetSizeAsync(installer.Url, cancellationToken);
                context.Log($"UniLab: baixando {installer.Url} ({(size is { } s ? $"{s / 1048576} MB" : "tamanho não informado")}).");

                var nextLog = 0L;
                await platform.Unilab.DownloadAsync(installer.Url, path, done =>
                {
                    if (size is not { } total || total <= 0 || done < nextLog)
                        return;
                    context.Log($"UniLab: {done * 100 / total}% baixado.");
                    nextLog = done + total / 4;
                }, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException)
            {
                return ProcedureResult.Fail("O download do UniLab não terminou. Confira a internet e tente de novo.", ex.Message);
            }

            var file = platform.Unilab.Inspect(path);
            if (UnilabRelease.Problem(file, size, installer) is { } problem)
                return ProcedureResult.Fail($"O instalador baixado não passou na conferência: {problem}. Nada foi instalado. Chame o suporte.");
            context.Log($"UniLab: arquivo conferido ({file.Company} / {file.Product} {file.ProductVersion}, {file.Length} bytes).");

            context.Log("UniLab: instalando em modo silencioso (/S).");
            var exitCode = await platform.Unilab.RunInstallerAsync(path, "/S", InstallTimeout, cancellationToken);
            if (exitCode is null)
                return ProcedureResult.Fail("O instalador do UniLab demorou demais e foi deixado de lado. Chame o suporte.");
            context.Log($"UniLab: instalador terminou (código {exitCode}).");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return ProcedureResult.Fail("O instalador do UniLab não pôde ser executado.", ex.Message);
        }
        finally
        {
            platform.Unilab.DeleteFolder(folder);
        }

        var installed = platform.Unilab.InstalledVersion();
        return UnilabRelease.IsUpToDate(installed, installer.Version)
            ? ProcedureResult.Ok($"UniLab {installed} instalado.")
            : ProcedureResult.Fail(
                $"O instalador terminou, mas o UniLab {installer.Version} não aparece instalado (encontrado: {installed ?? "nenhum"}). Chame o suporte.");
    }
}

/// <summary>
/// Preenche a licença no UniLab novo e liga o TSPrint embutido nele: servidor, código ADM, usuário RDP,
/// senha e as pastas root (A4), pdf e etiq (Zebra). Fecha o UniLab novo antes de gravar.
/// </summary>
public sealed class ConfigureUnilabProcedure(PlatformServices platform) : IProcedure
{
    internal static TimeSpan ListTimeout { get; set; } = TimeSpan.FromSeconds(20);
    private const string PdfPrinter = "Microsoft Print to PDF";

    public string Id => "unilab.configure";
    public string DisplayName => "Preenchendo a licença e configurando o TSPrint";
    public bool RequiresElevation => false;
    public TimeSpan Timeout => ListTimeout + PrintingConstants.TSPrintCloseTimeout + TimeSpan.FromSeconds(15);

    public async Task<ProcedureResult> ExecuteAsync(ProcedureContext context, CancellationToken cancellationToken)
    {
        if (context.State.GetText(UnilabState.License) is not { } license)
            return ProcedureResult.Fail("Licença não informada.");

        IReadOnlyList<InstalledPrinter> printers;
        var listing = Task.Run(platform.Printers.List, CancellationToken.None);
        try
        {
            printers = await listing.WaitAsync(ListTimeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            _ = listing.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
            printers = [];
            context.Log("Impressoras: o Windows não respondeu; as pastas do TSPrint ficam para configurar depois.");
        }

        // Impressoras: as do TSPrint antigo, se estiverem funcionando; senão, a única que estiver funcionando.
        var legacyPrinters = SafeRead(platform.TSPrintConfig.ReadPrinterConfig);
        var rules = new List<UnilabCloud.PrinterRule>();
        var notes = new List<string>();

        var legacyA4 = TSPrintFolderPrinter.PrinterOf(legacyPrinters, PrintingConstants.TSPrintDocumentFolders);
        var a4 = legacyA4 is not null ? DocumentPrinterSelector.Select(printers, legacyA4) : null;
        var a4Name = a4 is { AlreadyWorking: true } ? legacyA4 : (a4?.SwitchTo ?? DocumentPrinterSelector.SelectForEmptyTSPrint(printers).SwitchTo)?.Name;
        // As pastas root (A4) e etiq sempre existem; a impressora só entra se for encontrada.
        rules.Add(new("root", a4Name, Print: true));
        if (a4Name is null)
            notes.Add("impressora A4 não encontrada (nenhuma ou mais de uma funcionando); pasta root criada sem impressora");

        if (printers.FirstOrDefault(p => p.Name.Equals(PdfPrinter, StringComparison.OrdinalIgnoreCase)) is { } pdf)
            rules.Add(new("pdf", pdf.Name, Print: false));

        var zebra = LabelPrinterSelector.Select(printers, TSPrintLabelPrinter.From(legacyPrinters));
        rules.Add(new("etiq", zebra.Printer?.Name, Print: true));
        if (zebra.Printer is null)
            notes.Add(printers.Any(p => ZebraModel.LanguageOf(p.DriverName) is not null)
                ? "etiquetadora não definida (" + zebra.Message.TrimEnd('.') + "); pasta etiq criada sem impressora"
                : "etiquetadora não encontrada; pasta etiq criada sem impressora");

        var current = SafeRead(platform.Unilab.ReadCloudConfig);
        var result = UnilabCloud.Apply(current, license, UnilabCloud.Password(license), rules);
        if (result is null)
            return ProcedureResult.Fail("A configuração do UniLab não está num formato reconhecido. Chame o suporte.");

        foreach (var running in platform.Processes.FindInCurrentSession(UnilabCloud.ProcessName)
                     .Where(p => IsCloud(p.Path)))
        {
            if (!platform.Processes.Close(running.Id, PrintingConstants.TSPrintCloseTimeout))
                return ProcedureResult.Fail("Não foi possível fechar o UniLab para gravar a licença. Feche o UniLab e tente de novo.");
        }

        try
        {
            platform.Unilab.WriteCloudConfig(result.Json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ProcedureResult.Fail("Não foi possível gravar a licença no UniLab.", ex.Message);
        }

        context.Log($"UniLab: {(result.Changes.Count == 0 ? "configuração já estava certa" : "ajustado: " + string.Join(", ", result.Changes))}.");
        var summary = $"Licença {license} preenchida e TSPrint do UniLab ligado" +
                      (rules.Any(r => r.PrinterName is not null)
                          ? $" ({string.Join("; ", rules.Where(r => r.PrinterName is not null).Select(r => $"{r.Folder}: {r.PrinterName}"))})"
                          : "");
        return ProcedureResult.Ok(notes.Count == 0
            ? summary + "."
            : summary + $". Atenção: {string.Join("; ", notes)} — ajuste na aba TSPrint do UniLab.");
    }

    internal static bool IsCloud(string? path) =>
        path is not null && string.Equals(Path.GetFullPath(path), Path.GetFullPath(UnilabCloud.Executable), StringComparison.OrdinalIgnoreCase);

    private static string? SafeRead(Func<string?> read)
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException)
        {
            return null;
        }
    }
}

/// <summary>
/// Desliga o TSPrint antigo (os dois puxariam as mesmas impressões): fecha, tira da inicialização do
/// Windows e troca o atalho do UniLab antigo na área de trabalho pelo do novo. Os atalhos vão para a pasta
/// de cópias do app (dá para voltar).
/// </summary>
public sealed class DisableLegacyTSPrintProcedure(PlatformServices platform) : IProcedure
{
    public string Id => "unilab.legacy-tsprint";
    public string DisplayName => "Desligando o TSPrint antigo";
    public bool RequiresElevation => false;
    public TimeSpan Timeout => PrintingConstants.TSPrintCloseTimeout + TimeSpan.FromSeconds(15);

    public Task<ProcedureResult> ExecuteAsync(ProcedureContext context, CancellationToken cancellationToken)
    {
        var done = new List<string>();

        var running = platform.Processes.FindInCurrentSession(PrintingConstants.TSPrintProcessName);
        foreach (var process in running)
        {
            if (!platform.Processes.Close(process.Id, PrintingConstants.TSPrintCloseTimeout))
                return Task.FromResult(ProcedureResult.Fail("Não foi possível fechar o TSPrint antigo. Feche-o e tente de novo."));
        }
        if (running.Count > 0)
            done.Add("fechado");
        context.State.Set(PrintingState.TSPrintRunning, false);

        var startup = platform.Unilab.DisableLegacyStartup();
        if (startup.Count > 0)
        {
            done.Add("retirado da inicialização do Windows");
            context.Log($"Inicialização: atalhos guardados em Backups: {string.Join(", ", startup)}.");
        }

        var shortcuts = platform.Unilab.RemoveLegacyUnilabShortcuts();
        if (shortcuts.Count > 0)
            context.Log($"Área de trabalho: atalhos do UniLab antigo guardados em Backups: {string.Join(", ", shortcuts)}.");

        return Task.FromResult(ProcedureResult.Ok(done.Count == 0
            ? "O TSPrint antigo já estava desligado."
            : $"TSPrint antigo {string.Join(" e ", done)}."));
    }
}

/// <summary>Abre o UniLab novo para a atendente fazer o login e testar.</summary>
public sealed class OpenUnilabProcedure(PlatformServices platform) : IProcedure
{
    public string Id => "unilab.open";
    public string DisplayName => "Abrindo o UniLab";
    public bool RequiresElevation => false;
    public TimeSpan Timeout => PrintingConstants.TSPrintStartTimeout + TimeSpan.FromSeconds(10);

    public async Task<ProcedureResult> ExecuteAsync(ProcedureContext context, CancellationToken cancellationToken)
    {
        if (!platform.Processes.FileExists(UnilabCloud.Executable))
            return ProcedureResult.Fail("O UniLab novo não foi encontrado em C:\\Rede_Unilab\\Unilab. Chame o suporte.");

        if (platform.Processes.FindInCurrentSession(UnilabCloud.ProcessName).Any(p => ConfigureUnilabProcedure.IsCloud(p.Path)))
            return ProcedureResult.Ok("O UniLab já está aberto.");

        platform.Processes.Start(UnilabCloud.Executable);
        var deadline = platform.Now() + PrintingConstants.TSPrintStartTimeout;
        while (platform.Now() < deadline)
        {
            if (platform.Processes.FindInCurrentSession(UnilabCloud.ProcessName).Any(p => ConfigureUnilabProcedure.IsCloud(p.Path)))
                return ProcedureResult.Ok("UniLab aberto.");
            await Task.Delay(500, cancellationToken);
        }
        return ProcedureResult.Fail("O UniLab foi aberto, mas não apareceu. Abra pelo atalho na área de trabalho.");
    }
}
