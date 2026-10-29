using WPSupportTools.Core.Execution;
using WPSupportTools.Core.Logging;
using WPSupportTools.Core.Procedures;

namespace WPSupportTools.Core.Tests;

public class WorkerProtocolTests
{
    private const string Pipe = "WPSupportTools-00000000000000000000000000000000";
    private const string Empty = "e30=";   // "{}" em base64

    [Fact]
    public void Worker_arguments_round_trip()
    {
        var original = new WorkerArguments("printing.fix", [1, 2, 3], WorkerArguments.NewPipeName(), 4242,
            new Dictionary<string, string> { ["printing.target-printers"] = "Brother DCP-T820DW Printer (Copiar 1)\nZDesigner TLP 2844" });

        Assert.True(WorkerArguments.IsWorkerInvocation(original.ToArgumentList()));
        Assert.True(WorkerArguments.TryParse(original.ToArgumentList(), out var parsed));
        Assert.Equal(original.SequenceId, parsed.SequenceId);
        Assert.Equal(original.StepIndices, parsed.StepIndices);
        Assert.Equal(original.PipeName, parsed.PipeName);
        Assert.Equal(original.ParentProcessId, parsed.ParentProcessId);
        Assert.Equal(original.State, parsed.State);
    }

    [Theory]
    [InlineData("--worker", "--sequence", "printing.fix", "--steps", "1", "--state", Empty, "--pipe", Pipe)]
    [InlineData("--worker", "--sequence", "printing.fix;calc", "--steps", "1", "--state", Empty, "--pipe", Pipe, "--parent", "1")]
    [InlineData("--worker", "--sequence", "printing.fix", "--steps", "1;del", "--state", Empty, "--pipe", Pipe, "--parent", "1")]
    [InlineData("--worker", "--sequence", "printing.fix", "--steps", "", "--state", Empty, "--pipe", Pipe, "--parent", "1")]
    [InlineData("--worker", "--sequence", "printing.fix", "--steps", "1", "--state", "não-é-base64!", "--pipe", Pipe, "--parent", "1")]
    [InlineData("--worker", "--sequence", "printing.fix", "--steps", "1", "--state", "W10=", "--pipe", Pipe, "--parent", "1")]
    [InlineData("--worker", "--sequence", "printing.fix", "--steps", "1", "--state", Empty, "--pipe", "OutroPipe", "--parent", "1")]
    [InlineData("--worker", "--sequence", "printing.fix", "--steps", "1", "--state", Empty, "--pipe", Pipe, "--parent", "-1")]
    [InlineData("--worker", "--steps", "1", "--sequence", "printing.fix", "--state", Empty, "--pipe", Pipe, "--parent", "1")]
    public void Malformed_worker_arguments_are_rejected(params string[] args)
    {
        Assert.False(WorkerArguments.TryParse(args, out _));
    }

    [Theory]
    [InlineData(new[] { 1, 2, 3, 4, 5 }, true)]
    [InlineData(new[] { 3 }, true)]
    [InlineData(new[] { 0 }, false)]      // verificação da fila: não exige administrador
    [InlineData(new[] { 6 }, false)]      // conferir fila: não exige administrador
    [InlineData(new[] { 7, 8 }, false)]   // TSPrint: nunca roda como administrador
    [InlineData(new[] { 2, 1 }, false)]   // fora de ordem
    [InlineData(new[] { 1, 1 }, false)]   // repetido
    [InlineData(new[] { 9 }, false)]      // não existe
    public void Elevated_process_accepts_only_admin_steps_of_the_sequence(int[] steps, bool expected)
    {
        Assert.True(Modules.SequenceCatalog.TryCreate("printing.fix", out var sequence));
        Assert.Equal(expected, new WorkerArguments("printing.fix", steps, Pipe, 1).StepsAreValidFor(sequence));
    }

    [Fact]
    public void Elevated_process_refuses_state_keys_the_sequence_does_not_allow()
    {
        Assert.True(Modules.SequenceCatalog.TryCreate("printing.fix", out var sequence));
        var args = new WorkerArguments("printing.fix", [1], Pipe, 1,
            new Dictionary<string, string> { ["qualquer.coisa"] = "valor" });

        Assert.False(args.StepsAreValidFor(sequence));
    }

    [Fact]
    public void Run_events_survive_serialization()
    {
        RunEvent[] events =
        [
            new StepStartedEvent(0, "printing.check-spooler"),
            new StepFinishedEvent(1, "printing.stop-spooler", ProcedureResult.Fail("falhou", "Win32Exception: acesso negado")),
            new LogEvent(LogLevel.Warning, "atenção"),
            new StepsCompletedEvent([1, 2], [ProcedureResult.Ok("ok"), ProcedureResult.Fail("x")]),
        ];

        foreach (var original in events)
        {
            var copy = RunEventSerializer.TryDeserialize(RunEventSerializer.Serialize(original));
            Assert.NotNull(copy);
            Assert.Equal(original.GetType(), copy.GetType());
        }

        var done = Assert.IsType<StepsCompletedEvent>(
            RunEventSerializer.TryDeserialize(RunEventSerializer.Serialize(events[3])));
        Assert.Equal([1, 2], done.Indices);
        Assert.Equal(ProcedureOutcome.Failure, done.Results[1].Outcome);
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
