using WPSupportTools.Core.Execution;
using WPSupportTools.Core.Modules.Printing;
using WPSupportTools.Core.Modules.Printing.Correction;
using WPSupportTools.Core.Modules.Printing.Correction.Procedures;
using WPSupportTools.Core.Platform;
using WPSupportTools.Core.Procedures;

namespace WPSupportTools.Core.Tests;

public sealed class PrintingProcedureTests
{
    private readonly FakePlatform _fake = new();
    private readonly ProcedureContext _context = new(new DelegateRunEventSink(_ => { }), isElevated: true);

    private static PrintJobInfo Job(PrintJobStatus status = PrintJobStatus.None, DateTime? at = null) =>
        new("Samsung M408x Series", status, at);

    // ---------- Regra de "fila travada" ----------

    [Fact]
    public void Empty_queue_is_not_stuck() =>
        Assert.False(PrintQueueAnalyzer.Assess([]).IsStuck);

    [Fact]
    public void Slow_job_without_error_is_not_stuck_even_after_minutes() =>
        Assert.False(PrintQueueAnalyzer.Assess([Job(PrintJobStatus.Printing, _fake.Now.AddMinutes(-10))]).IsStuck);

    [Theory]
    [InlineData(PrintJobStatus.Error)]
    [InlineData(PrintJobStatus.Offline)]
    [InlineData(PrintJobStatus.Blocked)]
    [InlineData(PrintJobStatus.UserInterventionRequired | PrintJobStatus.Printing)]
    public void Job_with_problem_is_stuck(PrintJobStatus status) =>
        Assert.True(PrintQueueAnalyzer.Assess([Job(status, _fake.Now)]).IsStuck);

    // ---------- Verificar fila ----------

    [Fact]
    public async Task Check_fails_when_spooler_service_does_not_exist()
    {
        _fake.Services.Spooler = null;
        var result = await new CheckPrintQueueProcedure(_fake.Build()).ExecuteAsync(_context, default);
        Assert.Equal(ProcedureOutcome.Failure, result.Outcome);
    }

    [Fact]
    public async Task Stopped_spooler_leads_to_reset_path()
    {
        _fake.Services.Spooler = FakeServices.Running() with { State = ServiceState.Stopped };
        await new CheckPrintQueueProcedure(_fake.Build()).ExecuteAsync(_context, default);
        Assert.True(_context.State.IsSet(PrintingState.QueueNeedsReset));
    }

    // ---------- Parar / iniciar spooler ----------

    [Fact]
    public async Task Stop_refuses_when_dependent_services_are_running()
    {
        _fake.Services.Spooler = FakeServices.Running() with { RunningDependents = ["Fax"] };
        var result = await new StopPrintSpoolerProcedure(_fake.Build()).ExecuteAsync(_context, default);

        Assert.Equal(ProcedureOutcome.Failure, result.Outcome);
        Assert.Empty(_fake.Services.Calls);
    }

    [Fact]
    public async Task Stop_reports_timeout_as_failure()
    {
        _fake.Services.TimeOut = true;
        var result = await new StopPrintSpoolerProcedure(_fake.Build()).ExecuteAsync(_context, default);
        Assert.Equal(ProcedureOutcome.Failure, result.Outcome);
    }

    [Fact]
    public async Task Start_refuses_disabled_service_without_changing_it()
    {
        _fake.Services.Spooler = FakeServices.Running() with { State = ServiceState.Stopped, IsDisabled = true };
        var result = await new StartPrintSpoolerProcedure(_fake.Build()).ExecuteAsync(_context, default);

        Assert.Equal(ProcedureOutcome.Failure, result.Outcome);
        Assert.Empty(_fake.Services.Calls);
    }

    // ---------- Cancelar documentos travados ----------

    [Fact]
    public async Task Cancel_touches_only_printers_with_stuck_jobs()
    {
        _fake.Queue.InstalledPrinters.UnionWith(["Brother DCP-T820DW", "ZDesigner TLP 2844"]);
        _fake.Jobs.Add(new PrintJobInfo("Brother DCP-T820DW", PrintJobStatus.Error | PrintJobStatus.Printing, _fake.Now));
        _fake.Jobs.Add(new PrintJobInfo("ZDesigner TLP 2844", PrintJobStatus.Spooling, _fake.Now));

        var result = await new CancelStuckJobsProcedure(_fake.Build()).ExecuteAsync(_context, default);

        Assert.Equal(ProcedureOutcome.Success, result.Outcome);
        Assert.Equal(["Brother DCP-T820DW"], _fake.Queue.Cancelled);
        Assert.Single(_fake.Jobs, j => j.PrinterName == "ZDesigner TLP 2844");
    }

    [Fact]
    public async Task Cancel_fails_when_printer_cannot_be_found()
    {
        _fake.Jobs.Add(new PrintJobInfo("Impressora removida", PrintJobStatus.Error, _fake.Now));

        var result = await new CancelStuckJobsProcedure(_fake.Build()).ExecuteAsync(_context, default);

        Assert.Equal(ProcedureOutcome.Failure, result.Outcome);
    }

    [Fact]
    public async Task Cancel_does_nothing_when_spooler_is_stopped()
    {
        _fake.Services.Spooler = FakeServices.Running() with { State = ServiceState.Stopped };

        var result = await new CancelStuckJobsProcedure(_fake.Build()).ExecuteAsync(_context, default);

        Assert.Equal(ProcedureOutcome.Success, result.Outcome);
        Assert.Empty(_fake.Queue.Cancelled);
    }

    [Fact]
    public async Task Recheck_fails_when_a_job_is_still_stuck()
    {
        _fake.Jobs.Add(new PrintJobInfo("Brother DCP-T820DW", PrintJobStatus.Error, _fake.Now));

        var result = await new RecheckPrintQueueProcedure(_fake.Build()).ExecuteAsync(_context, default);

        Assert.Equal(ProcedureOutcome.Failure, result.Outcome);
        Assert.Contains("Brother DCP-T820DW", result.Message);
    }

    // ---------- TSPrint ----------

    [Fact]
    public async Task Start_tsprint_without_configured_path_does_nothing()
    {
        var result = await new StartTSPrintProcedure(_fake.Build(), null).ExecuteAsync(_context, default);

        Assert.Equal(ProcedureOutcome.NotConfigured, result.Outcome);
        Assert.Empty(_fake.Processes.Started);
    }

    [Fact]
    public async Task Start_tsprint_fails_when_executable_is_missing()
    {
        var result = await new StartTSPrintProcedure(_fake.Build(), @"C:\TSPrint\TSPrint.exe").ExecuteAsync(_context, default);

        Assert.Equal(ProcedureOutcome.Failure, result.Outcome);
        Assert.Empty(_fake.Processes.Started);
    }

    [Fact]
    public async Task Start_tsprint_opens_configured_executable()
    {
        const string path = @"C:\TSPrint\TSPrint.exe";
        _fake.Processes.TSPrintRunning = false;
        _fake.Processes.ExistingFiles.Add(path);

        var result = await new StartTSPrintProcedure(_fake.Build(), path).ExecuteAsync(_context, default);

        Assert.Equal(ProcedureOutcome.Success, result.Outcome);
        Assert.Equal([path], _fake.Processes.Started);
    }
}
