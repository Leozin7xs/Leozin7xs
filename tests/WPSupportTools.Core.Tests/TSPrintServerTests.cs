using WPSupportTools.Core.Execution;
using WPSupportTools.Core.Modules.Printing;
using WPSupportTools.Core.Modules.Printing.Correction;
using WPSupportTools.Core.Modules.Printing.Correction.Procedures;
using WPSupportTools.Core.Platform;
using WPSupportTools.Core.Procedures;

namespace WPSupportTools.Core.Tests;

/// <summary>Caso real: TSPrint "desconfigurado" (Privado, URL de exemplo, usuário 00000Cent00, sem senha).</summary>
public sealed class TSPrintServerTests : IDisposable
{
    private const string Token = "0A1B2C3D4E5F6071";
    private const string User = "01451cendocri21";

    // Valores da tela do TSPrint desconfigurado.
    private const string Broken =
        """{"servidor":0,"maxthreads":10,"url":"http://meudominio.com.br/impressao-ts","codigo_adm":"default","rdp_user":"00000Cent00","porta":80,"intervalo":5,"token":"","MainApp":"TSPrint","FingerPrint":0,"Scanner":0,"ConnectTimeout":15,"ReadTimeout":45,"ServerVersion":"1.7"}""";

    private const string Configured =
        """{"servidor":1,"maxthreads":20,"url":"http://imp.unilab.app.br/tsprint","codigo_adm":"01451","rdp_user":"01451cendocri21","porta":3080,"intervalo":2,"token":"0A1B2C3D4E5F6071","MainApp":"TSPrint","FingerPrint":0,"Scanner":0,"ConnectTimeout":15,"ReadTimeout":45,"ServerVersion":"1.7"}""";

    private readonly FakePlatform _fake = new();
    private readonly List<RunEvent> _events = [];

    public TSPrintServerTests() => _fake.Processes.ExistingFiles.Add(PrintingConstants.TSPrintExecutablePath!);

    public void Dispose() => _fake.Dispose();

    private Task<ProcedureResult> RunStepAsync(string? token = Token) =>
        new ConfigureTSPrintServerProcedure(_fake.Build(), PrintingConstants.TSPrintExecutablePath, token)
            .ExecuteAsync(new ProcedureContext(new DelegateRunEventSink(_events.Add), isElevated: false), default);

    private IEnumerable<string> Logs => _events.OfType<LogEvent>().Select(e => e.Message);

    [Fact]
    public void Broken_config_gets_the_wp_standard_and_keeps_the_other_fields()
    {
        var result = TSPrintServerSettings.Apply(Broken, User, Token)!;

        Assert.Equal(Configured, result.Json);
        Assert.Equal(["servidor", "maxthreads", "url", "codigo_adm", "porta", "intervalo", "rdp_user", "token"], result.ChangedFields);
    }

    [Fact]
    public void Configured_tsprint_has_nothing_to_change()
    {
        Assert.Empty(TSPrintServerSettings.Apply(Configured, User, Token)!.ChangedFields);
        Assert.Equal(User, TSPrintServerSettings.ConfiguredRdpUser(Configured));
        Assert.Null(TSPrintServerSettings.ConfiguredRdpUser(Broken));   // 00000Cent00 não conta
        Assert.False(TSPrintServerSettings.HasToken(Broken));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("lixo")]
    [InlineData("[1,2]")]
    public void Unrecognized_general_config_is_not_touched(string? config) =>
        Assert.Null(TSPrintServerSettings.Apply(config, User, Token));

    [Fact]
    public void Unilab_user_is_read_without_the_at_sign()
    {
        string[] ini = ["[CLOUD]", "Https=1", "Usuario=@01451cendocri21", "Url=https://exemplo", "[VersionLocal]", "Number=1.0"];

        Assert.Equal(User, UnilabUser.From(ini));
        Assert.Null(UnilabUser.From(["[CLOUD]", "Usuario=", "Url=x"]));
    }

    [Fact]
    public async Task Broken_tsprint_is_configured_with_tsprint_closed_and_reopened()
    {
        _fake.TSPrintGeneralConfig = Broken;
        _fake.UnilabUser = User;

        var result = await RunStepAsync();

        Assert.Equal(ProcedureOutcome.Success, result.Outcome);
        Assert.Equal(Configured, _fake.TSPrintGeneralConfig);
        Assert.Single(_fake.Processes.Closed);
        Assert.True(_fake.Processes.TSPrintRunning);
        Assert.DoesNotContain(Logs, l => l.Contains(Token));   // senha nunca no log
        Assert.Contains(Logs, l => l.Contains("senha") && l.Contains("usuário RDP"));
    }

    [Fact]
    public async Task Configured_tsprint_is_left_alone()
    {
        _fake.TSPrintGeneralConfig = Configured;

        var result = await RunStepAsync();

        Assert.Equal(ProcedureOutcome.Success, result.Outcome);
        Assert.Equal(0, _fake.TSPrintGeneralWrites);
        Assert.Empty(_fake.Processes.Closed);
    }

    [Fact]
    public async Task Existing_password_and_user_are_kept()
    {
        _fake.TSPrintGeneralConfig = Configured.Replace("\"servidor\":1", "\"servidor\":0");
        _fake.UnilabUser = "outro-usuario";

        await RunStepAsync(token: "FFFFFFFFFFFFFFFF");

        Assert.Equal(Configured, _fake.TSPrintGeneralConfig);
    }

    [Fact]
    public async Task Without_unilab_user_the_rest_is_configured_and_support_is_asked()
    {
        _fake.TSPrintGeneralConfig = Broken;

        var result = await RunStepAsync();

        Assert.Equal(ProcedureOutcome.Failure, result.Outcome);
        Assert.Contains("usuário RDP não foi encontrado", result.Message);
        Assert.Contains("\"servidor\":1", _fake.TSPrintGeneralConfig);
        Assert.Contains("\"rdp_user\":\"00000Cent00\"", _fake.TSPrintGeneralConfig);
    }

    [Fact]
    public async Task App_build_without_password_says_so()
    {
        _fake.TSPrintGeneralConfig = Broken;
        _fake.UnilabUser = User;

        var result = await RunStepAsync(token: null);

        Assert.Equal(ProcedureOutcome.Failure, result.Outcome);
        Assert.Contains("não tem a senha", result.Message);
        Assert.Contains("\"token\":\"\"", _fake.TSPrintGeneralConfig);
    }

    [Fact]
    public async Task Pc_without_tsprint_is_skipped()
    {
        var result = await RunStepAsync();

        Assert.Equal(ProcedureOutcome.NotNeeded, result.Outcome);
    }

    [Fact]
    public async Task After_reconfiguring_the_print_fix_creates_root_and_pdf_folders()
    {
        // O TSPrint perdeu as impressoras ao reconfigurar o servidor: arquivo de impressoras vazio.
        _fake.TSPrintGeneralConfig = Configured;
        _fake.TSPrintPrinterConfig = "[]";
        _fake.Printers.Add(new InstalledPrinter("Brother DCP-T830DW Printer", "Brother DCP-T830DW series", "USB001", false, true));
        _fake.Printers.Add(new InstalledPrinter("Microsoft Print to PDF", "Microsoft Print To PDF", "PORTPROMPT:", false, false));

        var report = await new SequenceRunner(isElevated: false)
            .RunAsync(PrintingFixSequence.Create(_fake.Build()), new DelegateRunEventSink(_events.Add));

        Assert.Equal(SequenceOutcome.Succeeded, report.Outcome);
        Assert.Equal(
            """[{"ID":1,"FOLDER":"root","PRINTERNAME":"Brother DCP-T830DW Printer","DOWNLOAD":"Sim","PRINT":"Sim"},{"ID":2,"FOLDER":"pdf","PRINTERNAME":"Microsoft Print to PDF","DOWNLOAD":"Sim","PRINT":"N""" + "\\u00E3o\"}]",
            _fake.TSPrintPrinterConfig);
    }

    [Fact]
    public void New_folders_copy_extra_fields_of_newer_tsprint_versions()
    {
        const string config = """[{"ID":1,"FOLDER":"root","PRINTERNAME":"A","DOWNLOAD":"Sim","PRINT":"Sim","ROTATE":"Nao"}]""";

        Assert.Equal(
            """[{"ID":1,"FOLDER":"root","PRINTERNAME":"A","DOWNLOAD":"Sim","PRINT":"Sim","ROTATE":"Nao"},{"ID":2,"FOLDER":"etiq","PRINTERNAME":"Z","DOWNLOAD":"Sim","PRINT":"Sim","ROTATE":"Nao"}]""",
            TSPrintFolderPrinter.WithFolderAdded(config, "etiq", "Z"));
    }
}
