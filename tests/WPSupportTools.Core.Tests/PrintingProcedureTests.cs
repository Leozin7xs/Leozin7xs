using WPSupportTools.Core.Execution;
using WPSupportTools.Core.Modules.Printing;
using WPSupportTools.Core.Modules.Printing.Correction;
using WPSupportTools.Core.Modules.Printing.Correction.Procedures;
using WPSupportTools.Core.Platform;
using WPSupportTools.Core.Procedures;

namespace WPSupportTools.Core.Tests;

public sealed class PrintingProcedureTests : IDisposable
{
    private readonly FakePlatform _fake = new();
    private readonly ProcedureContext _context = new(new DelegateRunEventSink(_ => { }), isElevated: true);

    public void Dispose() => _fake.Dispose();

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

    // ---------- Limpar fila ----------

    [Fact]
    public async Task Clear_deletes_only_spool_files_in_top_folder()
    {
        _fake.Services.Spooler = FakeServices.Running() with { State = ServiceState.Stopped };
        var spl = _fake.AddSpoolFile("00012.SPL");
        var shd = _fake.AddSpoolFile("00012.shd");
        var other = _fake.AddSpoolFile("leia-me.txt");
        var sub = Directory.CreateDirectory(Path.Combine(_fake.SpoolFolder, "sub"));
        File.WriteAllText(Path.Combine(sub.FullName, "00099.SPL"), "x");

        var result = await new ClearPrintQueueProcedure(_fake.Build()).ExecuteAsync(_context, default);

        Assert.Equal(ProcedureOutcome.Success, result.Outcome);
        Assert.False(File.Exists(spl));
        Assert.False(File.Exists(shd));
        Assert.True(File.Exists(other));
        Assert.True(File.Exists(Path.Combine(sub.FullName, "00099.SPL")));
    }

    [Fact]
    public async Task Clear_refuses_while_spooler_is_running()
    {
        var spl = _fake.AddSpoolFile("00012.SPL");
        var result = await new ClearPrintQueueProcedure(_fake.Build()).ExecuteAsync(_context, default);

        Assert.Equal(ProcedureOutcome.Failure, result.Outcome);
        Assert.True(File.Exists(spl));
    }

    [Fact]
    public async Task Clear_refuses_non_standard_spool_folder()
    {
        _fake.Services.Spooler = FakeServices.Running() with { State = ServiceState.Stopped };
        _fake.ConfiguredSpoolFolder = Path.Combine(Path.GetTempPath(), "outra-pasta");
        var spl = _fake.AddSpoolFile("00012.SPL");

        var result = await new ClearPrintQueueProcedure(_fake.Build()).ExecuteAsync(_context, default);

        Assert.Equal(ProcedureOutcome.Failure, result.Outcome);
        Assert.True(File.Exists(spl));
    }

    [Fact]
    public async Task Clear_fails_when_folder_is_missing()
    {
        _fake.Services.Spooler = FakeServices.Running() with { State = ServiceState.Stopped };
        Directory.Delete(_fake.SpoolFolder);

        var result = await new ClearPrintQueueProcedure(_fake.Build()).ExecuteAsync(_context, default);

        Assert.Equal(ProcedureOutcome.Failure, result.Outcome);
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
