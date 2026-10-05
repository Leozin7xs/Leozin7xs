using WPSupportTools.Core.Execution;
using WPSupportTools.Core.Logging;
using WPSupportTools.Core.Procedures;

namespace WPSupportTools.Core.Tests;

public class WorkerProtocolTests
{
    [Fact]
    public void Worker_arguments_round_trip()
    {
        var original = new WorkerArguments("printing.fix", WorkerArguments.NewPipeName(), 4242);

        Assert.True(WorkerArguments.IsWorkerInvocation(original.ToArgumentList()));
        Assert.True(WorkerArguments.TryParse(original.ToArgumentList(), out var parsed));
        Assert.Equal(original, parsed);
    }

    [Theory]
    [InlineData("--worker", "--sequence", "printing.fix", "--pipe", "WPSupportTools-00000000000000000000000000000000")]
    [InlineData("--worker", "--sequence", "printing.fix;calc", "--pipe", "WPSupportTools-00000000000000000000000000000000", "--parent", "1")]
    [InlineData("--worker", "--sequence", "printing.fix", "--pipe", "OutroPipe", "--parent", "1")]
    [InlineData("--worker", "--sequence", "printing.fix", "--pipe", "WPSupportTools-00000000000000000000000000000000", "--parent", "-1")]
    [InlineData("--worker", "--pipe", "WPSupportTools-00000000000000000000000000000000", "--sequence", "printing.fix", "--parent", "1")]
    public void Malformed_worker_arguments_are_rejected(params string[] args)
    {
        Assert.False(WorkerArguments.TryParse(args, out _));
    }

    [Fact]
    public void Run_events_survive_serialization()
    {
        RunEvent[] events =
        [
            new StepStartedEvent(0, "printing.check-spooler"),
            new StepFinishedEvent(1, "printing.stop-spooler", ProcedureResult.Fail("falhou", "Win32Exception: acesso negado")),
            new LogEvent(LogLevel.Warning, "atenção"),
            new RunCompletedEvent(new SequenceReport("printing.fix", SequenceOutcome.Failed,
                [ProcedureResult.Ok("ok"), ProcedureResult.Fail("x")], 1)),
        ];

        foreach (var original in events)
        {
            var copy = RunEventSerializer.TryDeserialize(RunEventSerializer.Serialize(original));
            Assert.NotNull(copy);
            Assert.Equal(original.GetType(), copy.GetType());
        }

        var report = Assert.IsType<RunCompletedEvent>(
            RunEventSerializer.TryDeserialize(RunEventSerializer.Serialize(events[3]))).Report;
        Assert.Equal(SequenceOutcome.Failed, report.Outcome);
        Assert.Equal(1, report.FailedStepIndex);
        Assert.Equal(2, report.Results.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("não é json")]
    [InlineData("{\"type\":\"desconhecido\"}")]
    public void Invalid_lines_are_ignored(string line)
    {
        Assert.Null(RunEventSerializer.TryDeserialize(line));
    }
}
