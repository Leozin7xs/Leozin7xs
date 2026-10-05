using WPSupportTools.Core.Execution;
using WPSupportTools.Core.Procedures;

namespace WPSupportTools.Core.Tests;

public class SequenceRunnerTests
{
    private readonly List<RunEvent> _events = [];
    private IRunEventSink Sink => new DelegateRunEventSink(_events.Add);

    private static MaintenanceSequence Sequence(params IProcedure[] steps) => new("test.sequence", "Teste", steps);

    [Fact]
    public async Task Runs_all_steps_in_order_when_everything_succeeds()
    {
        var order = new List<string>();
        FakeProcedure Track(string id) => new(id, (_, _) => { order.Add(id); return Task.FromResult(ProcedureResult.Ok("ok")); });

        var report = await new SequenceRunner(isElevated: false)
            .RunAsync(Sequence(Track("a"), Track("b"), Track("c")), Sink);

        Assert.Equal(SequenceOutcome.Succeeded, report.Outcome);
        Assert.Equal(["a", "b", "c"], order);
        Assert.Null(report.FailedStepIndex);
        Assert.Equal(6, _events.Count(e => e is StepStartedEvent or StepFinishedEvent));
    }

    [Fact]
    public async Task Stops_at_first_failure_and_skips_remaining_steps()
    {
        var after = FakeProcedure.Ok("after");

        var report = await new SequenceRunner(isElevated: false)
            .RunAsync(Sequence(FakeProcedure.Ok("first"), FakeProcedure.Failing("broken"), after), Sink);

        Assert.Equal(SequenceOutcome.Failed, report.Outcome);
        Assert.Equal(1, report.FailedStepIndex);
        Assert.Equal(0, after.Executions);
        Assert.Equal(ProcedureOutcome.Skipped, report.Results[2].Outcome);
        Assert.DoesNotContain(_events, e => e is StepStartedEvent { ProcedureId: "after" });
    }

    [Fact]
    public async Task Exception_becomes_failure_with_error_detail()
    {
        var report = await new SequenceRunner(isElevated: false)
            .RunAsync(Sequence(FakeProcedure.Throwing("boom", new InvalidOperationException("quebrou"))), Sink);

        Assert.Equal(SequenceOutcome.Failed, report.Outcome);
        Assert.Equal("InvalidOperationException: quebrou", report.Results[0].ErrorDetail);
    }

    [Fact]
    public async Task Access_denied_is_reported_as_elevation_required()
    {
        var report = await new SequenceRunner(isElevated: false)
            .RunAsync(Sequence(FakeProcedure.Throwing("denied", new UnauthorizedAccessException())), Sink);

        Assert.Equal(SequenceOutcome.ElevationRequired, report.Outcome);
    }

    [Fact]
    public async Task Elevated_step_is_not_executed_in_a_non_elevated_process()
    {
        var admin = FakeProcedure.Ok("admin", requiresElevation: true);

        var report = await new SequenceRunner(isElevated: false).RunAsync(Sequence(admin), Sink);

        Assert.Equal(SequenceOutcome.ElevationRequired, report.Outcome);
        Assert.Equal(0, admin.Executions);
    }

    [Fact]
    public async Task Elevated_step_runs_in_an_elevated_process()
    {
        var admin = FakeProcedure.Ok("admin", requiresElevation: true);

        var report = await new SequenceRunner(isElevated: true).RunAsync(Sequence(admin), Sink);

        Assert.Equal(SequenceOutcome.Succeeded, report.Outcome);
        Assert.Equal(1, admin.Executions);
    }

    [Fact]
    public async Task Step_exceeding_its_timeout_fails()
    {
        var slow = new FakeProcedure("slow",
            async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return ProcedureResult.Ok("nunca"); },
            timeout: TimeSpan.FromMilliseconds(50));

        var report = await new SequenceRunner(isElevated: false).RunAsync(Sequence(slow), Sink);

        Assert.Equal(SequenceOutcome.Failed, report.Outcome);
        Assert.Contains("Tempo limite", report.Results[0].Message);
    }

    [Fact]
    public async Task Not_configured_steps_continue_and_mark_run_as_simulated()
    {
        var real = FakeProcedure.Ok("real");
        var placeholder = new FakeProcedure("placeholder", (_, _) => Task.FromResult(ProcedureResult.NotConfigured("sim")));

        var report = await new SequenceRunner(isElevated: false).RunAsync(Sequence(placeholder, real), Sink);

        Assert.Equal(SequenceOutcome.Simulated, report.Outcome);
        Assert.Equal(1, real.Executions);
    }

    [Fact]
    public async Task Procedure_log_messages_are_forwarded()
    {
        var chatty = new FakeProcedure("chatty", (ctx, _) => { ctx.Log("detalhe"); return Task.FromResult(ProcedureResult.Ok("ok")); });

        await new SequenceRunner(isElevated: false).RunAsync(Sequence(chatty), Sink);

        Assert.Contains(_events, e => e is LogEvent { Message: "detalhe" });
    }

    [Fact]
    public void Sequence_rejects_duplicate_procedures()
    {
        Assert.Throws<ArgumentException>(() => Sequence(FakeProcedure.Ok("x"), FakeProcedure.Ok("x")));
    }
}
