using WPSupportTools.Core.Execution;
using WPSupportTools.Core.Modules;
using WPSupportTools.Core.Modules.Printing.Labels;
using WPSupportTools.Core.Platform;
using WPSupportTools.Core.Procedures;

namespace WPSupportTools.Core.Tests;

public sealed class LabelTests : IDisposable
{
    private const string Zd230 = "ZDesigner ZD230-203dpi ZPL";
    private const string Gc420 = "ZDesigner GC420t (EPL)";

    private readonly FakePlatform _fake = new();
    private readonly List<RunEvent> _events = [];

    public void Dispose() => _fake.Dispose();

    private static InstalledPrinter Printer(string name, string driver, bool offline = false, bool isDefault = false) =>
        new(name, driver, "USB001", offline, isDefault);

    private Task<SequenceReport> RunAsync(MaintenanceSequence sequence) =>
        new SequenceRunner(isElevated: false).RunAsync(sequence, new DelegateRunEventSink(_events.Add));

    // ---------- Modelo / linguagem ----------

    [Theory]
    [InlineData("ZDesigner GC420t (EPL)", ZebraLanguage.Epl)]
    [InlineData("ZDesigner TLP 2844", ZebraLanguage.Epl)]
    [InlineData("ZDesigner ZD230-203dpi ZPL", ZebraLanguage.Zpl)]
    [InlineData("ZDesigner ZD220-203dpi ZPL", ZebraLanguage.Zpl)]
    [InlineData("ZDesigner GC420t", ZebraLanguage.Zpl)]
    public void Recognizes_driver_language(string driver, ZebraLanguage expected) =>
        Assert.Equal(expected, ZebraModel.LanguageOf(driver));

    [Theory]
    [InlineData("Brother DCP-T820DW Printer")]
    [InlineData("Microsoft Print To PDF")]
    public void Non_zebra_drivers_are_ignored(string driver) => Assert.Null(ZebraModel.LanguageOf(driver));

    [Fact]
    public void Commands_match_the_language()
    {
        Assert.Contains("~JC", System.Text.Encoding.ASCII.GetString(ZebraModel.CalibrationCommand(ZebraLanguage.Zpl)));
        Assert.Contains("xa", System.Text.Encoding.ASCII.GetString(ZebraModel.CalibrationCommand(ZebraLanguage.Epl)));
        Assert.StartsWith("^XA", System.Text.Encoding.ASCII.GetString(ZebraModel.TestLabel(ZebraLanguage.Zpl)));
        Assert.Contains("P1", System.Text.Encoding.ASCII.GetString(ZebraModel.TestLabel(ZebraLanguage.Epl)));
    }

    // ---------- Escolha da etiquetadora ----------

    [Fact]
    public void Picks_the_only_online_zebra_ignoring_offline_copies()
    {
        var choice = LabelPrinterSelector.Select(
        [
            Printer("Brother", "Brother DCP-T820DW Printer"),
            Printer("ZDesigner TLP 2844", "ZDesigner TLP 2844", offline: true),
            Printer("ZDesigner TLP 2844 (Copiar 2)", "ZDesigner TLP 2844"),
        ]);

        Assert.Equal("ZDesigner TLP 2844 (Copiar 2)", choice.Printer?.Name);
        Assert.Equal(ZebraLanguage.Epl, choice.Language);
    }

    [Fact]
    public void All_zebras_offline_means_disconnected() =>
        Assert.Contains("desligada ou desconectada",
            LabelPrinterSelector.Select([Printer("Z", Zd230, offline: true)]).Message);

    [Fact]
    public void Several_online_zebras_use_the_default_one() =>
        Assert.Equal("B", LabelPrinterSelector.Select(
            [Printer("A", Zd230), Printer("B", Zd230, isDefault: true)]).Printer?.Name);

    [Fact]
    public void Several_online_zebras_without_default_are_not_guessed() =>
        Assert.Null(LabelPrinterSelector.Select([Printer("A", Zd230), Printer("B", Gc420)]).Printer);

    [Fact]
    public void Several_online_zebras_use_the_one_configured_in_tsprint_before_the_default()
    {
        var choice = LabelPrinterSelector.Select(
            [Printer("Zebra TS&", Zd230, isDefault: true), Printer("ZDesigner GC420d (EPL)", Gc420)],
            tsprintLabelPrinter: "zdesigner gc420d (epl)");

        Assert.Equal("ZDesigner GC420d (EPL)", choice.Printer?.Name);
        Assert.Equal(ZebraLanguage.Epl, choice.Language);
        Assert.Contains("configurada no TSPrint", choice.Message);
    }

    [Fact]
    public void Tsprint_printer_offline_falls_back_to_the_windows_default() =>
        Assert.Equal("B", LabelPrinterSelector.Select(
            [Printer("A", Zd230), Printer("B", Zd230, isDefault: true), Printer("C", Gc420, offline: true)],
            tsprintLabelPrinter: "C").Printer?.Name);

    [Fact]
    public void Tsprint_printer_not_among_online_ones_is_reported()
    {
        var choice = LabelPrinterSelector.Select([Printer("A", Zd230), Printer("B", Gc420)], tsprintLabelPrinter: "Outra");

        Assert.Null(choice.Printer);
        Assert.Contains("\"Outra\"", choice.Message);
    }

    [Fact]
    public async Task Windows_not_answering_the_printer_list_fails_with_guidance_instead_of_hanging()
    {
        using var hang = new ManualResetEventSlim(false);
        _fake.PrinterListBlocksUntil = hang;
        _fake.Printers.Add(Printer(Zd230, Zd230));
        var previous = Modules.Printing.Labels.Procedures.LocateLabelPrinterProcedure.ListTimeout;
        Modules.Printing.Labels.Procedures.LocateLabelPrinterProcedure.ListTimeout = TimeSpan.FromMilliseconds(200);
        try
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var report = await RunAsync(LabelSequences.CreateNotPrinting(_fake.Build()));

            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5));
            Assert.Equal(SequenceOutcome.Failed, report.Outcome);
            Assert.Equal(0, report.FailedStepIndex);
            Assert.Contains("Impressora A4 não está imprimindo", report.Results[0].Message);
            Assert.Empty(_fake.Raw.Sent);
        }
        finally
        {
            Modules.Printing.Labels.Procedures.LocateLabelPrinterProcedure.ListTimeout = previous;
            hang.Set();
        }
    }

    // ---------- Arquivo do TSPrint (print_doc_cnf.data) ----------

    private const string TSPrintConfig =
        "﻿[{\"ID\":1,\"FOLDER\":\"root\",\"PRINTERNAME\":\"SEC30CDA7FFC122\",\"DOWNLOAD\":\"Sim\",\"PRINT\":\"Sim\"}," +
        "{\"ID\":2,\"FOLDER\":\"pdf\",\"PRINTERNAME\":\"Microsoft Print to PDF\",\"DOWNLOAD\":\"Sim\",\"PRINT\":\"N\\u00E3o\"}," +
        "{\"ID\":3,\"FOLDER\":\"etiq\",\"PRINTERNAME\":\"ZDesigner TLP 2844 (Copiar 5)\",\"DOWNLOAD\":\"Sim\",\"PRINT\":\"Sim\"}]";

    [Fact]
    public void Reads_the_label_printer_from_tsprint_config() =>
        Assert.Equal("ZDesigner TLP 2844 (Copiar 5)", TSPrintLabelPrinter.From(TSPrintConfig));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("lixo")]
    [InlineData("{\"FOLDER\":\"etiq\",\"PRINTERNAME\":\"X\"}")]
    [InlineData("[{\"ID\":1,\"FOLDER\":\"root\",\"PRINTERNAME\":\"X\"}]")]
    [InlineData("[{\"ID\":3,\"FOLDER\":\"etiq\",\"PRINTERNAME\":\"\"}]")]
    public void Unrecognized_tsprint_config_gives_no_printer(string? config) =>
        Assert.Null(TSPrintLabelPrinter.From(config));

    [Fact]
    public async Task Two_online_zebras_are_resolved_by_the_tsprint_config()
    {
        // Caso real: "Zebra TS&" e "ZDesigner GC420d (EPL)" ligadas, nenhuma padrão do Windows.
        const string gc420d = "ZDesigner GC420d (EPL)";
        _fake.Printers.Add(Printer("Zebra TS&", Zd230));
        _fake.Printers.Add(Printer(gc420d, gc420d));
        _fake.Preferences.DevModes[gc420d] = FakePreferences.NewDevMode(gc420d, 508, 254);
        _fake.TSPrintPrinterConfig = TSPrintConfig.Replace("ZDesigner TLP 2844 (Copiar 5)", gc420d);

        var report = await RunAsync(LabelSequences.CreateMisconfigured(_fake.Build()));

        Assert.Equal(SequenceOutcome.Succeeded, report.Outcome);
        Assert.All(_fake.Raw.Sent, s => Assert.Equal(gc420d, s.Printer));
    }

    [Fact]
    public async Task Two_online_zebras_without_tsprint_config_still_stop()
    {
        _fake.Printers.Add(Printer("A", Zd230));
        _fake.Printers.Add(Printer("B", Gc420));

        var report = await RunAsync(LabelSequences.CreateMisconfigured(_fake.Build()));

        Assert.Equal(SequenceOutcome.Failed, report.Outcome);
        Assert.Equal(0, report.FailedStepIndex);
        Assert.Empty(_fake.Raw.Sent);
    }

    // ---------- Selecionar a etiquetadora no TSPrint ----------

    private const string OldTSPrintPrinter = "ZDesigner TLP 2844 (Copiar 5)";
    private static string TSPrintExe => Modules.Printing.PrintingConstants.TSPrintExecutablePath!;

    [Fact]
    public void Changing_the_tsprint_label_printer_keeps_the_rest_of_the_file_identical()
    {
        var updated = TSPrintLabelPrinter.WithPrinter(TSPrintConfig, Zd230);

        Assert.Equal(TSPrintConfig.TrimStart('﻿').Replace(OldTSPrintPrinter, Zd230), updated);
        Assert.Equal(Zd230, TSPrintLabelPrinter.From(updated));
    }

    [Fact]
    public void Folder_named_etiqueta_is_also_the_label_folder()
    {
        var config = TSPrintConfig.Replace("\"etiq\"", "\"Etiqueta\"");

        Assert.Equal(OldTSPrintPrinter, TSPrintLabelPrinter.From(config));
        Assert.Equal(Zd230, TSPrintLabelPrinter.From(TSPrintLabelPrinter.WithPrinter(config, Zd230)));
    }

    [Fact]
    public void Printer_names_with_accents_are_written_like_tsprint_writes_them()
    {
        var updated = TSPrintLabelPrinter.WithPrinter(TSPrintConfig, "Zebra Recepção \"2\"");

        Assert.Contains("""
            "PRINTERNAME":"Zebra Recep\u00E7\u00E3o \"2\""
            """, updated);
        Assert.Equal("Zebra Recepção \"2\"", TSPrintLabelPrinter.From(updated));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("lixo")]
    [InlineData("[{\"ID\":1,\"FOLDER\":\"root\",\"PRINTERNAME\":\"X\"}]")]
    [InlineData("[{\"ID\":3,\"FOLDER\":\"etiq\"}]")]
    public void Tsprint_config_without_label_folder_is_not_changed(string? config) =>
        Assert.Null(TSPrintLabelPrinter.WithPrinter(config, Zd230));

    private void TSPrintPointsToARemovedPrinter()
    {
        _fake.Printers.Add(Printer(Zd230, Zd230));          // a única Zebra ligada
        _fake.TSPrintPrinterConfig = TSPrintConfig;        // TSPrint ainda aponta para a "(Copiar 5)", que não existe mais
        _fake.Processes.ExistingFiles.Add(TSPrintExe);
    }

    [Fact]
    public async Task Tsprint_pointing_to_another_printer_is_switched_to_the_zebra_and_reopened()
    {
        TSPrintPointsToARemovedPrinter();

        var report = await RunAsync(LabelSequences.CreateNotPrinting(_fake.Build()));

        Assert.Equal(SequenceOutcome.Succeeded, report.Outcome);
        Assert.Equal(Zd230, TSPrintLabelPrinter.From(_fake.TSPrintPrinterConfig));
        Assert.Contains("agora vão para", report.Results[9].Message);
        Assert.Single(_fake.Processes.Closed);
        Assert.Equal([TSPrintExe], _fake.Processes.Started);
        Assert.True(_fake.Processes.TSPrintRunning);
        Assert.Single(_fake.Raw.Sent);   // etiqueta de teste
    }

    [Fact]
    public async Task Tsprint_already_on_the_zebra_is_left_untouched()
    {
        TSPrintPointsToARemovedPrinter();
        _fake.TSPrintPrinterConfig = TSPrintConfig.Replace(OldTSPrintPrinter, Zd230);

        var report = await RunAsync(LabelSequences.CreateNotPrinting(_fake.Build()));

        Assert.Equal(SequenceOutcome.Succeeded, report.Outcome);
        Assert.Contains("já imprime", report.Results[9].Message);
        Assert.Equal(0, _fake.TSPrintConfigWrites);
        Assert.Empty(_fake.Processes.Closed);
        Assert.Empty(_fake.Processes.Started);
    }

    [Fact]
    public async Task Closed_tsprint_gets_the_zebra_and_is_opened_once()
    {
        TSPrintPointsToARemovedPrinter();
        _fake.Processes.TSPrintRunning = false;

        var report = await RunAsync(LabelSequences.CreateNotPrinting(_fake.Build()));

        Assert.Equal(SequenceOutcome.Succeeded, report.Outcome);
        Assert.Equal(Zd230, TSPrintLabelPrinter.From(_fake.TSPrintPrinterConfig));
        Assert.Empty(_fake.Processes.Closed);
        Assert.Equal([TSPrintExe], _fake.Processes.Started);   // aberto pela etapa "Abrindo o TSPrint"
    }

    [Fact]
    public async Task Misconfigured_flow_also_switches_the_tsprint_label_printer()
    {
        TSPrintPointsToARemovedPrinter();
        _fake.Preferences.DevModes[Zd230] = FakePreferences.NewDevMode(Zd230, 508, 254);

        var report = await RunAsync(LabelSequences.CreateMisconfigured(_fake.Build()));

        Assert.Equal(SequenceOutcome.Succeeded, report.Outcome);
        Assert.Equal(Zd230, TSPrintLabelPrinter.From(_fake.TSPrintPrinterConfig));
    }

    [Fact]
    public async Task Tsprint_that_does_not_close_is_not_changed()
    {
        TSPrintPointsToARemovedPrinter();
        _fake.Processes.ClosesSuccessfully = false;

        var report = await RunAsync(LabelSequences.CreateNotPrinting(_fake.Build()));

        Assert.Equal(SequenceOutcome.Failed, report.Outcome);
        Assert.Equal(9, report.FailedStepIndex);
        Assert.Contains("Feche o TSPrint", report.Results[9].Message);
        Assert.Equal(0, _fake.TSPrintConfigWrites);
        Assert.Empty(_fake.Raw.Sent);
    }

    [Fact]
    public async Task Tsprint_config_that_cannot_be_written_fails_and_reopens_tsprint()
    {
        TSPrintPointsToARemovedPrinter();
        _fake.TSPrintConfigReadOnly = true;

        var report = await RunAsync(LabelSequences.CreateNotPrinting(_fake.Build()));

        Assert.Equal(SequenceOutcome.Failed, report.Outcome);
        Assert.Contains("Nada foi alterado", report.Results[9].Message);
        Assert.Equal(TSPrintConfig, _fake.TSPrintPrinterConfig);
        Assert.True(_fake.Processes.TSPrintRunning);
    }

    [Fact]
    public async Task Tsprint_without_label_folder_gets_the_etiq_folder_created()
    {
        TSPrintPointsToARemovedPrinter();
        _fake.TSPrintPrinterConfig = "[{\"ID\":1,\"FOLDER\":\"root\",\"PRINTERNAME\":\"SEC30CDA7FFC122\",\"DOWNLOAD\":\"Sim\",\"PRINT\":\"Sim\"}]";

        var report = await RunAsync(LabelSequences.CreateNotPrinting(_fake.Build()));

        Assert.Equal(SequenceOutcome.Succeeded, report.Outcome);
        Assert.Equal(
            "[{\"ID\":1,\"FOLDER\":\"root\",\"PRINTERNAME\":\"SEC30CDA7FFC122\",\"DOWNLOAD\":\"Sim\",\"PRINT\":\"Sim\"}," +
            $"{{\"ID\":2,\"FOLDER\":\"etiq\",\"PRINTERNAME\":\"{Zd230}\",\"DOWNLOAD\":\"Sim\",\"PRINT\":\"Sim\"}}]",
            _fake.TSPrintPrinterConfig);
    }

    // ---------- DEVMODE ----------

    [Fact]
    public void Changing_paper_preserves_driver_private_data()
    {
        var original = FakePreferences.NewDevMode(Zd230, 1040, 508, orientation: 2);
        var changed = new DevMode(original).WithPaper(508, 254, DevMode.OrientationPortrait);

        Assert.Equal(508, changed.PaperWidth);
        Assert.Equal(254, changed.PaperLength);
        Assert.Equal(DevMode.OrientationPortrait, changed.Orientation);
        Assert.Equal(Zd230, changed.DeviceName);
        Assert.Equal(original[220..], changed.ToArray()[220..]);
    }

    // ---------- "Sai desconfigurada" ----------

    [Fact]
    public async Task Misconfigured_size_is_fixed_then_printer_is_calibrated_and_tested()
    {
        _fake.Printers.Add(Printer(Zd230, Zd230));
        _fake.Preferences.DevModes[Zd230] = FakePreferences.NewDevMode(Zd230, 1040, 508);   // 104 × 50,8 mm

        var report = await RunAsync(LabelSequences.CreateMisconfigured(_fake.Build()));

        Assert.Equal(SequenceOutcome.Succeeded, report.Outcome);
        Assert.Equal([Zd230], _fake.Preferences.Writes);
        var fixedMode = new DevMode(_fake.Preferences.DevModes[Zd230]);
        Assert.Equal((508, 254), ((int)fixedMode.PaperWidth, (int)fixedMode.PaperLength));
        Assert.Equal(["~JC", "^XA"], _fake.Raw.Sent.Select(s => s.Text.Trim()[..3]));
    }

    [Fact]
    public async Task Standard_size_is_left_untouched()
    {
        _fake.Printers.Add(Printer(Zd230, Zd230));
        _fake.Preferences.DevModes[Zd230] = FakePreferences.NewDevMode(Zd230, 508, 254);

        var report = await RunAsync(LabelSequences.CreateMisconfigured(_fake.Build()));

        Assert.Equal(SequenceOutcome.Succeeded, report.Outcome);
        Assert.Empty(_fake.Preferences.Writes);
        Assert.Equal(ProcedureOutcome.NotNeeded, report.Results[2].Outcome);
        Assert.Equal(2, _fake.Raw.Sent.Count);
    }

    [Fact]
    public async Task Driver_refusing_the_size_fails_before_calibrating()
    {
        _fake.Printers.Add(Printer(Gc420, Gc420));
        _fake.Preferences.DevModes[Gc420] = FakePreferences.NewDevMode(Gc420, 1040, 508);
        _fake.Preferences.DriverIgnoresSize = true;

        var report = await RunAsync(LabelSequences.CreateMisconfigured(_fake.Build()));

        Assert.Equal(SequenceOutcome.Failed, report.Outcome);
        Assert.Equal(2, report.FailedStepIndex);
        Assert.Empty(_fake.Raw.Sent);
    }

    [Fact]
    public async Task Offline_label_printer_stops_without_sending_anything()
    {
        _fake.Printers.Add(Printer(Zd230, Zd230, offline: true));

        var report = await RunAsync(LabelSequences.CreateMisconfigured(_fake.Build()));

        Assert.Equal(SequenceOutcome.Failed, report.Outcome);
        Assert.Equal(0, report.FailedStepIndex);
        Assert.Contains("desligada ou desconectada", report.Results[0].Message);
        Assert.Empty(_fake.Raw.Sent);
        Assert.Empty(_fake.Preferences.Writes);
    }

    // ---------- "Não sai nada" ----------

    [Fact]
    public async Task Not_printing_ignores_stuck_jobs_of_other_printers()
    {
        _fake.Printers.Add(Printer(Zd230, Zd230));
        _fake.Jobs.Add(new PrintJobInfo("Brother DCP-T820DW", PrintJobStatus.Error, _fake.Now));

        var report = await RunAsync(LabelSequences.CreateNotPrinting(_fake.Build()));

        Assert.Equal(SequenceOutcome.Succeeded, report.Outcome);
        Assert.Equal(ProcedureOutcome.NotNeeded, report.Results[2].Outcome);   // não cancela nada
        Assert.Empty(_fake.Services.Calls);
        Assert.Single(_fake.Raw.Sent);                                          // etiqueta de teste
    }

    [Fact]
    public async Task Not_printing_with_stuck_label_job_asks_admin_only_for_queue_steps()
    {
        _fake.Printers.Add(Printer(Zd230, Zd230));
        _fake.Queue.InstalledPrinters.Add(Zd230);
        _fake.Jobs.Add(new PrintJobInfo(Zd230, PrintJobStatus.Error, _fake.Now));
        var elevated = new RecordingElevatedRunner();

        var report = await new SequenceRunner(isElevated: false, elevated)
            .RunAsync(LabelSequences.CreateNotPrinting(_fake.Build()), new DelegateRunEventSink(_events.Add));

        Assert.Equal(SequenceOutcome.Succeeded, report.Outcome);
        Assert.Equal([[2, 3, 4, 5, 6]], elevated.Requests);
        Assert.Equal([Zd230], _fake.Queue.Cancelled);
        Assert.Single(_fake.Raw.Sent);
    }

    [Fact]
    public async Task Not_printing_treats_any_waiting_label_job_as_stuck_and_touches_only_the_label_printer()
    {
        _fake.Printers.Add(Printer(Zd230, Zd230));
        _fake.Queue.InstalledPrinters.UnionWith([Zd230, "Brother DCP-T820DW"]);
        _fake.Jobs.Add(new PrintJobInfo(Zd230, PrintJobStatus.None, _fake.Now));                  // parado, sem erro
        _fake.Jobs.Add(new PrintJobInfo("Brother DCP-T820DW", PrintJobStatus.Error, _fake.Now));  // outra impressora
        var elevated = new RecordingElevatedRunner();

        var report = await new SequenceRunner(isElevated: false, elevated)
            .RunAsync(LabelSequences.CreateNotPrinting(_fake.Build()), new DelegateRunEventSink(_events.Add));

        Assert.Equal(SequenceOutcome.Succeeded, report.Outcome);
        Assert.Equal([Zd230], _fake.Queue.Cancelled.Distinct());
        Assert.Single(_fake.Jobs, j => j.PrinterName == "Brother DCP-T820DW");
    }

    [Fact]
    public async Task Not_printing_reports_failure_when_label_queue_is_not_cleared()
    {
        _fake.Printers.Add(Printer(Zd230, Zd230));
        _fake.Queue.InstalledPrinters.Add(Zd230);
        _fake.Queue.JobsSurviveCancel = true;
        _fake.Jobs.Add(new PrintJobInfo(Zd230, PrintJobStatus.Error, _fake.Now));

        var report = await new SequenceRunner(isElevated: false, new RecordingElevatedRunner())
            .RunAsync(LabelSequences.CreateNotPrinting(_fake.Build()), new DelegateRunEventSink(_events.Add));

        Assert.Equal(SequenceOutcome.Failed, report.Outcome);
        Assert.Equal(7, report.FailedStepIndex);
        Assert.Contains("não foi limpa", report.Results[7].Message);
        Assert.Equal(ServiceState.Running, _fake.Services.Spooler!.State);
        Assert.Empty(_fake.Raw.Sent);   // não manda etiqueta de teste para fila travada
    }

    [Fact]
    public async Task Not_printing_with_broken_queue_clears_only_the_label_printer_files()
    {
        _fake.Printers.Add(Printer(Zd230, Zd230));
        _fake.Queue.Unreadable = true;
        _fake.QueueFollowsSpoolFolder = true;
        _fake.AddSpoolJob(Zd230, 3);
        _fake.AddSpoolJob("Brother DCP-T820DW Printer", 4);
        File.WriteAllText(_fake.SpoolPath("00005.SPL"), "resto sem .SHD");

        var report = await new SequenceRunner(isElevated: false, new RecordingElevatedRunner())
            .RunAsync(LabelSequences.CreateNotPrinting(_fake.Build()), new DelegateRunEventSink(_events.Add));

        Assert.Equal(SequenceOutcome.Succeeded, report.Outcome);              // a fila da Zebra ficou vazia
        Assert.Equal(["00004.SHD", "00004.SPL"], _fake.SpoolFiles());         // documento da Brother não foi tocado
        Assert.Equal(ProcedureOutcome.Success, report.Results[4].Outcome);    // arquivos apagados
        Assert.Single(_fake.Raw.Sent);                                        // etiqueta de teste
    }

    [Fact]
    public void Label_sequences_are_registered_and_offered_as_choices()
    {
        Assert.True(SequenceCatalog.TryCreate(LabelSequences.NotPrintingId, out _));
        Assert.True(SequenceCatalog.TryCreate(LabelSequences.MisconfiguredId, out _));

        var labels = ModuleCatalog.All.SelectMany(m => m.Actions).Single(a => a.Choices is not null);
        Assert.Equal([LabelSequences.NotPrintingId, LabelSequences.MisconfiguredId],
            labels.Choices!.Select(c => c.SequenceId));
    }

    [Theory]
    [InlineData(new[] { 2, 3, 4, 5, 6 }, true)]
    [InlineData(new[] { 0 }, false)]
    [InlineData(new[] { 7 }, false)]
    [InlineData(new[] { 10 }, false)]
    public void Elevated_process_accepts_only_queue_steps_of_label_sequence(int[] steps, bool expected)
    {
        Assert.True(SequenceCatalog.TryCreate(LabelSequences.NotPrintingId, out var sequence));
        Assert.Equal(expected,
            new WorkerArguments(LabelSequences.NotPrintingId, steps, "WPSupportTools-00000000000000000000000000000000", 1)
                .StepsAreValidFor(sequence));
    }

    private sealed class RecordingElevatedRunner : IElevatedStepRunner
    {
        public List<int[]> Requests { get; } = [];

        public async Task<IReadOnlyList<ProcedureResult>> RunAsync(
            MaintenanceSequence sequence, IReadOnlyList<int> stepIndices, IReadOnlyDictionary<string, string> state,
            IRunEventSink sink, CancellationToken ct)
        {
            Requests.Add([.. stepIndices]);
            return await new SequenceRunner(isElevated: true).RunStepsAsync(sequence, stepIndices, sink, ct, state);
        }
    }
}
