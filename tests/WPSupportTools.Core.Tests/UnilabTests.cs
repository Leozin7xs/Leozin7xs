using System.Text;
using System.Text.Json.Nodes;
using WPSupportTools.Core.Execution;
using WPSupportTools.Core.Modules.Printing;
using WPSupportTools.Core.Modules.Printing.Correction;
using WPSupportTools.Core.Modules.Printing.Labels.Procedures;
using WPSupportTools.Core.Modules.Unilab;
using WPSupportTools.Core.Platform;
using WPSupportTools.Core.Procedures;

namespace WPSupportTools.Core.Tests;

public sealed class UnilabTests : IDisposable
{
    private const string License = "@01451exemplo01";
    private const string Brother = "Brother DCP-T830DW Printer";
    private const string Zd230 = "ZDesigner ZD230-203dpi ZPL";
    private static readonly string Hex = Convert.ToHexString(Encoding.UTF8.GetBytes("S3nh4WPx"));

    private readonly FakePlatform _fake = new();
    private readonly ElevatedRecorder _elevated = new();

    public void Dispose() => _fake.Dispose();

    // ---------- Licença ----------

    [Theory]
    [InlineData("@01451exemplo01", "@01451exemplo01")]
    [InlineData("01451exemplo01", "@01451exemplo01")]
    [InlineData("  @01451cendocri21 ", "@01451cendocri21")]
    public void License_is_normalized(string typed, string expected) =>
        Assert.Equal(expected, UnilabCloud.NormalizeLicense(typed).Value);

    [Theory]
    [InlineData("")]
    [InlineData("@abc")]
    [InlineData("@0145")]
    [InlineData("@01451 ex\"emplo")]
    public void Invalid_license_is_refused_with_a_message(string typed)
    {
        var (value, error) = UnilabCloud.NormalizeLicense(typed);
        Assert.Null(value);
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void Tsprint_identity_comes_from_the_license()
    {
        Assert.Equal("01451", UnilabCloud.AdminCode(License));
        Assert.Equal("01451exemplo01", UnilabCloud.RdpUser(License));
        Assert.Equal("01451aaa", UnilabCloud.Password(License));   // padrão da Uniware, o que o servidor aceita
    }

    // ---------- Página e instalador ----------

    [Fact]
    public void Newest_windows_installer_is_chosen_x64_when_possible()
    {
        const string html = """
            <a href="UniSenha.exe">x</a><a href="Unilab-0.1.58-win.exe">x</a><a href="Unilab-0.1.58-win-x64.exe">x</a>
            <a href="Unilab-0.1.60-win.exe">x</a><a href="Unilab-0.1.60-win-x64.exe">x</a><a href="Unilab-0.1.60-mac-x64.dmg">x</a>
            """;
        var installers = UnilabRelease.Parse(html);

        Assert.Equal("Unilab-0.1.60-win-x64.exe", UnilabRelease.Choose(installers, is64BitWindows: true)!.FileName);
        Assert.Equal("Unilab-0.1.60-win.exe", UnilabRelease.Choose(installers, is64BitWindows: false)!.FileName);
        Assert.Equal(new Uri("https://uniware.com.br/updates/cloud/novo/Unilab-0.1.60-win-x64.exe"),
            UnilabRelease.Choose(installers, true)!.Url);
        Assert.Null(UnilabRelease.Choose(UnilabRelease.Parse("<a href=\"UniSenha.exe\">"), true));
    }

    [Theory]
    [InlineData(108_367_491, "UNIWARE", "Unilab", "0.1.58", null)]
    [InlineData(1_000, "UNIWARE", "Unilab", "0.1.58", "KB")]
    [InlineData(108_367_490, "UNIWARE", "Unilab", "0.1.58", "diferente do anunciado")]
    [InlineData(108_367_491, "OUTRA", "Unilab", "0.1.58", "fabricante")]
    [InlineData(108_367_491, "UNIWARE", "Outro", "0.1.58", "produto")]
    [InlineData(108_367_491, "UNIWARE", "Unilab", "0.1.57", "versão interna")]
    public void Downloaded_installer_is_checked(long size, string company, string product, string version, string? problem)
    {
        var installer = new UnilabInstaller("Unilab-0.1.58-win-x64.exe", new Version(0, 1, 58), true);
        var result = UnilabRelease.Problem(new ExecutableInfo(size, company, product, version), 108_367_491, installer);

        if (problem is null)
            Assert.Null(result);
        else
            Assert.Contains(problem, result);
    }

    [Theory]
    [InlineData("Unilab 0.1.58", true)]
    [InlineData("Unilab", true)]
    [InlineData("UniSenha", false)]
    [InlineData("Unilab Antigo Configurador", false)]
    public void Installed_unilab_is_recognized_by_name(string name, bool expected) =>
        Assert.Equal(expected, UnilabRelease.IsUnilabDisplayName(name));

    // ---------- config.json ----------

    [Fact]
    public void Config_keeps_the_fields_of_the_app_and_turns_tsprint_on()
    {
        const string fromApp = """
            {"user":"","environment":"https","tabs":[{"id":"unilab"}],"updater":{"channel":"latest"},
             "tsprint":{"enabled":false,"codAdm":"","rdpUser":"","password":"","pdfRules":[{"folder":"root","printerName":"","download":false,"print":false,"rotate180":false,"fitToPaper":"none"}],"textRules":[]}}
            """;

        var result = UnilabCloud.Apply(fromApp, License, "S3nh4WPx",
            [new("root", Brother, true), new("etiq", Zd230, true)])!;
        var config = JsonNode.Parse(result.Json)!;

        Assert.Equal(License, (string?)config["user"]);
        Assert.True((bool)config["startMaximized"]!);                            // "Iniciar maximizado" sempre marcado
        Assert.Equal("unilab", (string?)config["tabs"]![0]!["id"]);              // campo do app preservado
        Assert.True((bool)config["tsprint"]!["enabled"]!);
        Assert.Equal("01451", (string?)config["tsprint"]!["codAdm"]);
        Assert.Equal("01451exemplo01", (string?)config["tsprint"]!["rdpUser"]);
        Assert.Equal("S3nh4WPx", (string?)config["tsprint"]!["password"]);
        Assert.Equal(Brother, UnilabCloud.PrinterOf(result.Json, ["root"]));
        Assert.Equal(Zd230, UnilabCloud.PrinterOf(result.Json, ["etiq"]));
        Assert.Contains("\n  \"user\"", result.Json);                             // mesmo recuo do UniLab
        Assert.Empty(UnilabCloud.Apply(result.Json, License, "S3nh4WPx",
            [new("root", Brother, true), new("etiq", Zd230, true)])!.Changes);
    }

    [Fact]
    public void Folders_without_printer_are_created_paused_and_existing_ones_are_kept()
    {
        var result = UnilabCloud.Apply(null, License, "S3nh4WPx", [new("root", null, true), new("etiq", null, true)])!;
        var rules = JsonNode.Parse(result.Json)!["tsprint"]!["pdfRules"]!.AsArray();

        foreach (var folder in new[] { "root", "etiq" })
        {
            var rule = rules.Single(r => (string?)r!["folder"] == folder)!;
            Assert.Equal("", (string?)rule["printerName"]);
            Assert.False((bool)rule["download"]!);     // sem impressora não baixa nem imprime:
            Assert.False((bool)rule["print"]!);        // iria para a impressora padrão do Windows
        }
        Assert.Contains("pasta etiq criada (sem impressora)", result.Changes);

        // Pasta já configurada não perde a impressora quando nenhuma é encontrada...
        var configured = UnilabCloud.Apply(result.Json, License, "S3nh4WPx", [new("etiq", Zd230, true)])!;
        Assert.Empty(UnilabCloud.Apply(configured.Json, License, "S3nh4WPx", [new("etiq", null, true)])!.Changes);
        // ...e, quando a impressora aparece, é conectada e passa a baixar e imprimir.
        var etiq = JsonNode.Parse(configured.Json)!["tsprint"]!["pdfRules"]!.AsArray().Single(r => (string?)r!["folder"] == "etiq")!;
        Assert.Equal(Zd230, (string?)etiq["printerName"]);
        Assert.True((bool)etiq["download"]! && (bool)etiq["print"]!);
    }

    [Fact]
    public async Task Pc_without_a4_or_label_printer_gets_the_folders_without_printers()
    {
        _fake.TSPrintGeneralConfig = $$"""{"servidor":1,"token":"{{Hex}}"}""";

        var report = await InstallAsync();

        Assert.Equal(SequenceOutcome.Succeeded, report.Outcome);
        var rules = JsonNode.Parse(_fake.Unilab.CloudConfig!)!["tsprint"]!["pdfRules"]!.AsArray();
        Assert.Equal(["root", "etiq"], rules.Select(r => (string)r!["folder"]!));
        Assert.All(rules, r => Assert.Equal("", (string?)r!["printerName"]));
        Assert.Contains(report.Results, r => r.Message.Contains("pasta etiq criada sem impressora"));
    }

    // ---------- Fluxo "Instalar o UniLab" ----------

    private void FreshPcWithOldTSPrint()
    {
        _fake.Printers.Add(new InstalledPrinter(Brother, "Brother DCP-T830DW series", "USB001", false, true));
        _fake.Printers.Add(new InstalledPrinter("Microsoft Print to PDF", "Microsoft Print To PDF", "PORTPROMPT:", false, false));
        _fake.Printers.Add(new InstalledPrinter(Zd230, Zd230, "USB002", false, false));
        _fake.Preferences.DevModes[Zd230] = FakePreferences.NewZebraDevMode(Zd230, 1040, 508, interpolation: 0);
        _fake.TSPrintGeneralConfig = $$"""{"servidor":1,"token":"{{Hex}}"}""";
        _fake.Processes.TSPrintRunning = true;
    }

    private Task<SequenceReport> InstallAsync(string license = License)
    {
        var sequence = UnilabModule.CreateInstall(_fake.Build());
        sequence.Inputs[UnilabState.License] = license;
        _fake.Processes.ExistingFiles.Add(UnilabCloud.Executable);   // o instalador "cria" o UniLab
        return new SequenceRunner(isElevated: false, _elevated).RunAsync(sequence, new DelegateRunEventSink(_ => { }));
    }

    [Fact]
    public async Task Fresh_pc_gets_unilab_installed_licensed_with_tsprint_and_old_tsprint_off()
    {
        FreshPcWithOldTSPrint();

        var report = await InstallAsync();

        Assert.Equal(SequenceOutcome.Succeeded, report.Outcome);

        // Download e instalação só no processo de administrador, e só com nome e versão vindos de fora.
        Assert.Equal([[2]], _elevated.Requests);
        Assert.Equal([UnilabState.InstallerFile, UnilabState.Version], _elevated.States.Single().Keys.Order());
        Assert.Equal([new Uri("https://uniware.com.br/updates/cloud/novo/Unilab-0.1.58-win-x64.exe")], _fake.Unilab.Downloads);
        Assert.Equal(["Unilab-0.1.58-win-x64.exe /S"], _fake.Unilab.InstallerRuns);
        Assert.Equal(_fake.Unilab.FoldersCreated, _fake.Unilab.FoldersDeleted);

        var config = _fake.Unilab.CloudConfig!;
        Assert.Equal(License, UnilabCloud.LicenseOf(config));
        Assert.Equal(Brother, UnilabCloud.PrinterOf(config, ["root"]));
        Assert.Equal("Microsoft Print to PDF", UnilabCloud.PrinterOf(config, ["pdf"]));
        Assert.Equal(Zd230, UnilabCloud.PrinterOf(config, ["etiq"]));
        Assert.True(CheckLabelSizeProcedure.IsStandard(_fake.Preferences.DevModes[Zd230]));   // tamanho e Clipart
        Assert.Equal("01451aaa", (string?)JsonNode.Parse(config)!["tsprint"]!["password"]);   // não a do TSPrint antigo

        Assert.False(_fake.Processes.TSPrintRunning);                  // TSPrint antigo fechado
        Assert.Empty(_fake.Unilab.LegacyStartup);                     // e fora da inicialização
        Assert.Empty(_fake.Unilab.LegacyDesktop);
        Assert.True(_fake.Processes.UnilabCloudRunning);              // UniLab aberto para testar
    }

    [Fact]
    public async Task Unilab_already_up_to_date_is_not_downloaded_but_gets_the_license()
    {
        FreshPcWithOldTSPrint();
        _fake.Unilab.Installed = "0.1.58";

        var report = await InstallAsync();

        Assert.Equal(SequenceOutcome.Succeeded, report.Outcome);
        Assert.Empty(_elevated.Requests);
        Assert.Empty(_fake.Unilab.Downloads);
        Assert.Equal(License, UnilabCloud.LicenseOf(_fake.Unilab.CloudConfig));
    }

    [Fact]
    public async Task Installer_that_fails_the_check_is_never_run()
    {
        FreshPcWithOldTSPrint();
        _fake.Unilab.Downloaded = new ExecutableInfo(108_367_491, "OUTRA EMPRESA", "Unilab", "0.1.58");

        var report = await InstallAsync();

        Assert.Equal(SequenceOutcome.Failed, report.Outcome);
        Assert.Equal(2, report.FailedStepIndex);
        Assert.Contains("fabricante", report.Results[2].Message);
        Assert.Empty(_fake.Unilab.InstallerRuns);
        Assert.Equal(_fake.Unilab.FoldersCreated, _fake.Unilab.FoldersDeleted);
        Assert.Null(_fake.Unilab.CloudConfig);
        Assert.True(_fake.Processes.TSPrintRunning);                  // nada mexido no TSPrint antigo
    }

    [Fact]
    public async Task Without_internet_nothing_changes()
    {
        FreshPcWithOldTSPrint();
        _fake.Unilab.Offline = true;

        var report = await InstallAsync();

        Assert.Equal(1, report.FailedStepIndex);
        Assert.Contains("site da Uniware", report.Results[1].Message);
    }

    [Fact]
    public async Task Invalid_license_stops_before_anything()
    {
        var report = await InstallAsync("abc");

        Assert.Equal(0, report.FailedStepIndex);
        Assert.Empty(_fake.Unilab.Downloads);
    }

    [Fact]
    public void Administrator_step_only_accepts_official_installer_names()
    {
        var state = new RunState();
        state.SetText(UnilabState.Version, "0.1.58");

        state.SetText(UnilabState.InstallerFile, @"..\..\Windows\evil.exe");
        Assert.Null(UnilabState.Installer(state));
        state.SetText(UnilabState.InstallerFile, "Unilab-0.1.58-win-x64.exe");
        Assert.NotNull(UnilabState.Installer(state));
    }

    // ---------- Botões de impressora num PC com o UniLab novo ----------

    [Fact]
    public async Task A4_button_on_new_unilab_switches_the_rule_in_unilab_and_leaves_old_tsprint_off()
    {
        _fake.Processes.ExistingFiles.Add(UnilabCloud.Executable);
        _fake.Processes.TSPrintRunning = false;
        _fake.Processes.UnilabCloudRunning = true;
        _fake.Printers.Add(new InstalledPrinter("Brother DCP-T820DW", "Microsoft IPP Class Driver", "WSD-1", true, false));
        _fake.Printers.Add(new InstalledPrinter(Brother, "Brother DCP-T830DW series", "USB001", false, true));
        _fake.Unilab.CloudConfig = UnilabCloud.Apply(null, License, UnilabCloud.Password(License), [new("root", "Brother DCP-T820DW", true)])!.Json;

        var report = await new SequenceRunner(isElevated: false)
            .RunAsync(PrintingFixSequence.Create(_fake.Build()), new DelegateRunEventSink(_ => { }));

        Assert.Equal(SequenceOutcome.Succeeded, report.Outcome);
        Assert.Equal(Brother, UnilabCloud.PrinterOf(_fake.Unilab.CloudConfig, ["root"]));
        Assert.Contains("UniLab reaberto", report.Results[8].Message);
        Assert.True(_fake.Processes.UnilabCloudRunning);
        Assert.False(_fake.Processes.TSPrintRunning);                 // o antigo continua desligado
        Assert.DoesNotContain(@"C:\Rede_Unilab\TSPrint\TSPrint.exe", _fake.Processes.Started);
    }

    [Fact]
    public async Task New_unilab_without_license_asks_to_use_the_install_button()
    {
        _fake.Processes.ExistingFiles.Add(UnilabCloud.Executable);
        _fake.Unilab.CloudConfig = UnilabCloud.Apply(null, null, null, [])!.Json;

        var report = await new SequenceRunner(isElevated: false)
            .RunAsync(PrintingFixSequence.Create(_fake.Build()), new DelegateRunEventSink(_ => { }));

        Assert.Equal(7, report.FailedStepIndex);
        Assert.Contains("Instalar o UniLab", report.Results[7].Message);
    }

    private sealed class ElevatedRecorder : IElevatedStepRunner
    {
        public List<int[]> Requests { get; } = [];
        public List<IReadOnlyDictionary<string, string>> States { get; } = [];

        public async Task<IReadOnlyList<ProcedureResult>> RunAsync(
            MaintenanceSequence sequence, IReadOnlyList<int> stepIndices, IReadOnlyDictionary<string, string> state,
            IRunEventSink sink, CancellationToken ct)
        {
            Requests.Add([.. stepIndices]);
            States.Add(state);
            Assert.True(ElevatedRequest.StepsAreValid(sequence, stepIndices, state));   // o processo de administrador aceitaria
            return await new SequenceRunner(isElevated: true).RunStepsAsync(sequence, stepIndices, sink, ct, state);
        }
    }
}
