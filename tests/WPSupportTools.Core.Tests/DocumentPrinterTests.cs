using WPSupportTools.Core.Execution;
using WPSupportTools.Core.Modules.Printing;
using WPSupportTools.Core.Modules.Printing.Correction;
using WPSupportTools.Core.Platform;
using WPSupportTools.Core.Procedures;

namespace WPSupportTools.Core.Tests;

/// <summary>Caso real: duas Brother no PC, o TSPrint apontando para a que não funciona.</summary>
public sealed class DocumentPrinterTests : IDisposable
{
    private const string T820 = "Brother DCP-T820DW";
    private const string T830 = "Brother DCP-T830DW Printer";
    private const string Zebra = @"\192.168.1.6\ZDesigner ZD220-203dpi ZPL";

    private const string Config =
        "[{\"ID\":1,\"FOLDER\":\"root\",\"PRINTERNAME\":\"Brother DCP-T820DW\",\"DOWNLOAD\":\"Sim\",\"PRINT\":\"Sim\"}," +
        "{\"ID\":2,\"FOLDER\":\"pdf\",\"PRINTERNAME\":\"Microsoft Print to PDF\",\"DOWNLOAD\":\"Sim\",\"PRINT\":\"N\u00E3o\"}," +
        """{"ID":3,"FOLDER":"etiq","PRINTERNAME":"\\\\192.168.1.6\\ZDesigner ZD220-203dpi ZPL","DOWNLOAD":"Sim","PRINT":"Sim"}]""";

    private readonly FakePlatform _fake = new();

    public void Dispose() => _fake.Dispose();

    private static InstalledPrinter P(string name, string driver, string port, bool offline = false, bool isDefault = false,
        string? problem = null) =>
        new(name, driver, port, offline, isDefault, problem);

    private static List<InstalledPrinter> PcFromTheScreenshot(bool t820Offline = true, bool t830Offline = false) =>
    [
        P(T820, "Microsoft IPP Class Driver", "WSD-2bd4239d", t820Offline, isDefault: true),
        P(T830, "Brother DCP-T830DW series", "USB001", t830Offline),
        P("Fax", "Microsoft Shared Fax Driver", "SHRFAX:"),
        P("Microsoft Print to PDF", "Microsoft Print To PDF", "PORTPROMPT:"),
        P("Microsoft XPS Document Writer", "Microsoft XPS Document Writer v4", "PORTPROMPT:"),
        P("OneNote (Desktop)", "Send to Microsoft OneNote 16 Driver", "nul:"),
        P("OneNote for Windows 10", "Microsoft Software Printer Driver", "Microsoft.Office.OneNote_16001"),
        P(Zebra, "ZDesigner ZD220-203dpi ZPL", "USB001"),
    ];

    [Fact]
    public void Tsprint_printer_with_error_is_replaced_by_the_one_that_works()
    {
        var choice = DocumentPrinterSelector.Select(PcFromTheScreenshot(), T820);

        Assert.Equal(T830, choice.SwitchTo?.Name);
        Assert.Contains("desligada ou com erro", choice.Message);
    }

    [Fact]
    public void Working_tsprint_printer_is_kept_even_with_another_one_working()
    {
        var choice = DocumentPrinterSelector.Select(PcFromTheScreenshot(t820Offline: false), T820);

        Assert.Null(choice.SwitchTo);
        Assert.True(choice.AlreadyWorking);
    }

    [Fact]
    public void Tsprint_printer_that_is_not_installed_is_replaced()
    {
        var choice = DocumentPrinterSelector.Select(PcFromTheScreenshot(), "Samsung antiga");

        Assert.Equal(T830, choice.SwitchTo?.Name);
        Assert.Contains("não está instalada", choice.Message);
    }

    [Fact]
    public void Nothing_working_changes_nothing_and_says_to_check_the_printer()
    {
        var choice = DocumentPrinterSelector.Select(PcFromTheScreenshot(t830Offline: true), T820);

        Assert.Null(choice.SwitchTo);
        Assert.False(choice.AlreadyWorking);
        Assert.Contains("ligada e conectada", choice.Message);
    }

    [Fact]
    public void Two_working_alternatives_are_not_guessed()
    {
        var printers = PcFromTheScreenshot();
        printers.Add(P("Samsung M408x Series", "Samsung Universal Print Driver", "IP_192.168.1.20"));

        var choice = DocumentPrinterSelector.Select(printers, T820);

        Assert.Null(choice.SwitchTo);
        Assert.True(choice.Ambiguous);
        Assert.Contains("Chame o suporte", choice.Message);
    }

    [Fact]
    public void Tsprint_printer_without_paper_is_not_swapped_and_the_message_says_why()
    {
        var printers = PcFromTheScreenshot(t820Offline: false);
        printers[0] = printers[0] with { Problem = "sem papel" };

        var choice = DocumentPrinterSelector.Select(printers, T820);

        Assert.Null(choice.SwitchTo);
        Assert.False(choice.AlreadyWorking);
        Assert.Equal($"A impressora {T820} está sem papel. Resolva na impressora e tente imprimir de novo.", choice.Message);
    }

    [Fact]
    public void Alternative_that_needs_attention_is_not_chosen()
    {
        var printers = PcFromTheScreenshot(t830Offline: false);
        printers[1] = printers[1] with { Problem = "com papel atolado" };

        Assert.Null(DocumentPrinterSelector.Select(printers, T820).SwitchTo);
    }

    [Theory]
    [InlineData(4, "sem papel")]
    [InlineData(8, "com papel atolado")]
    [InlineData(3, null)]   // pouco papel: só aviso
    [InlineData(0, null)]
    public void Windows_error_states_become_readable_problems(int state, string? expected) =>
        Assert.Equal(expected, WmiPrinterCatalog.ProblemOf((ushort)state));

    [Fact]
    public void Virtual_printers_and_zebras_are_never_candidates()
    {
        var physical = PcFromTheScreenshot().Where(DocumentPrinterSelector.IsPhysicalDocumentPrinter).Select(p => p.Name);
        Assert.Equal([T820, T830], physical);
    }

    private Task<SequenceReport> RunFixAsync() =>
        new SequenceRunner(isElevated: false)
            .RunAsync(PrintingFixSequence.Create(_fake.Build()), new DelegateRunEventSink(_ => { }));

    [Fact]
    public async Task Print_fix_switches_only_the_a4_printer_in_tsprint_and_reopens_it()
    {
        _fake.Printers.AddRange(PcFromTheScreenshot());
        _fake.TSPrintPrinterConfig = Config;
        _fake.Processes.ExistingFiles.Add(PrintingConstants.TSPrintExecutablePath!);

        var report = await RunFixAsync();

        Assert.Equal(SequenceOutcome.Succeeded, report.Outcome);
        Assert.Equal(T830, TSPrintFolderPrinter.PrinterOf(_fake.TSPrintPrinterConfig, PrintingConstants.TSPrintDocumentFolders));
        Assert.Equal(Config.Replace("\"PRINTERNAME\":\"Brother DCP-T820DW\"", $"\"PRINTERNAME\":\"{T830}\""), _fake.TSPrintPrinterConfig);
        Assert.Contains("documentos A4 agora vão para", report.Results[7].Message);
        Assert.Single(_fake.Processes.Closed);
        Assert.True(_fake.Processes.TSPrintRunning);
    }

    [Fact]
    public async Task Print_fix_with_two_working_alternatives_stops_without_touching_tsprint()
    {
        _fake.Printers.AddRange(PcFromTheScreenshot());
        _fake.Printers.Add(P("Samsung M408x Series", "Samsung Universal Print Driver", "IP_192.168.1.20"));
        _fake.TSPrintPrinterConfig = Config;

        var report = await RunFixAsync();

        Assert.Equal(SequenceOutcome.Failed, report.Outcome);
        Assert.Equal(7, report.FailedStepIndex);
        Assert.Equal(0, _fake.TSPrintConfigWrites);
        Assert.Empty(_fake.Processes.Closed);
    }
}
