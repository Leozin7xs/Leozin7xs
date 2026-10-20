using WPSupportTools.Core.Execution;
using WPSupportTools.Core.Modules;
using WPSupportTools.Core.Modules.Printing.Correction;
using WPSupportTools.Core.Platform;
using WPSupportTools.Core.Procedures;

namespace WPSupportTools.Core.Tests;

/// <summary>O fluxo completo "Corrigir problema de impressão", com a interface sem administrador.</summary>
public sealed class PrintingFlowTests : IDisposable
{
    private readonly FakePlatform _fake = new();
    private readonly FakeElevatedRunner _elevated = new();
    private readonly List<RunEvent> _events = [];

    public void Dispose() => _fake.Dispose();

    private Task<SequenceReport> RunAsync() =>
        new SequenceRunner(isElevated: false, _elevated)
            .RunAsync(PrintingFixSequence.Create(_fake.Build()), new DelegateRunEventSink(_events.Add));

    private static ProcedureOutcome[] Outcomes(SequenceReport report) => report.Results.Select(r => r.Outcome).ToArray();

    [Fact]
    public async Task Stuck_queue_resets_spooler_with_a_single_elevation()
    {
        _fake.Jobs.Add(new PrintJobInfo("Samsung", PrintJobStatus.Error, _fake.Now));
        var spl = _fake.AddSpoolFile("00001.SPL");

        var report = await RunAsync();

        Assert.Equal(SequenceOutcome.Succeeded, report.Outcome);
        Assert.Equal([[1, 2, 3]], _elevated.Requests);
        Assert.Equal(["stop Spooler", "start Spooler"], _fake.Services.Calls);
        Assert.False(File.Exists(spl));
        Assert.Equal(
            [ProcedureOutcome.Success, ProcedureOutcome.Success, ProcedureOutcome.Success, ProcedureOutcome.Success,
             ProcedureOutcome.NotNeeded, ProcedureOutcome.NotNeeded],
            Outcomes(report));
    }

    [Fact]
    public async Task Normal_queue_with_tsprint_open_changes_nothing_and_never_asks_for_admin()
    {
        var report = await RunAsync();

        Assert.Equal(SequenceOutcome.Succeeded, report.Outcome);
        Assert.Empty(_elevated.Requests);
        Assert.Empty(_fake.Services.Calls);
        Assert.Empty(_fake.Processes.Started);
        Assert.Equal(ProcedureOutcome.NotNeeded, report.Results[5].Outcome);
    }

    [Fact]
    public async Task Normal_queue_with_tsprint_closed_tries_to_open_it()
    {
        _fake.Processes.TSPrintRunning = false;

        var report = await RunAsync();

        Assert.Empty(_elevated.Requests);
        // Caminho do TSPrint ainda não configurado: etapa simulada, nada aberto.
        Assert.Equal(SequenceOutcome.Simulated, report.Outcome);
        Assert.Equal(ProcedureOutcome.NotConfigured, report.Results[5].Outcome);
        Assert.Empty(_fake.Processes.Started);
    }

    [Fact]
    public async Task Denied_elevation_stops_the_flow_without_changes()
    {
        _fake.Jobs.Add(new PrintJobInfo("Samsung", PrintJobStatus.Error, _fake.Now));
        _elevated.Deny = true;

        var report = await RunAsync();

        Assert.Equal(SequenceOutcome.ElevationRequired, report.Outcome);
        Assert.Equal(1, report.FailedStepIndex);
        Assert.Empty(_fake.Services.Calls);
        Assert.Equal(ProcedureOutcome.Skipped, report.Results[4].Outcome);
    }

    [Fact]
    public async Task Failure_while_stopping_spooler_skips_clearing_and_starting()
    {
        _fake.Jobs.Add(new PrintJobInfo("Samsung", PrintJobStatus.Error, _fake.Now));
        _fake.Services.Spooler = FakeServices.Running() with { RunningDependents = ["Fax"] };
        var spl = _fake.AddSpoolFile("00001.SPL");

        var report = await RunAsync();

        Assert.Equal(SequenceOutcome.Failed, report.Outcome);
        Assert.Equal(1, report.FailedStepIndex);
        Assert.Equal(ProcedureOutcome.Skipped, report.Results[2].Outcome);
        Assert.True(File.Exists(spl));
    }

    [Fact]
    public void Printing_sequence_is_in_the_catalog_and_every_action_points_to_one()
    {
        Assert.True(SequenceCatalog.TryCreate(PrintingFixSequence.Id, out _));
        foreach (var action in ModuleCatalog.All.SelectMany(m => m.Actions))
            Assert.True(SequenceCatalog.TryCreate(action.SequenceId, out _), action.SequenceId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("printing.FIX")]
    [InlineData("powershell -c whoami")]
    public void Unknown_sequence_ids_are_rejected(string id) =>
        Assert.False(SequenceCatalog.TryCreate(id, out _));

    /// <summary>Simula o processo administrador: roda o bloco como elevado, no mesmo processo.</summary>
    private sealed class FakeElevatedRunner : IElevatedStepRunner
    {
        public List<int[]> Requests { get; } = [];
        public bool Deny { get; set; }

        public async Task<IReadOnlyList<ProcedureResult>> RunAsync(
            MaintenanceSequence sequence, IReadOnlyList<int> stepIndices, IRunEventSink sink,
            CancellationToken cancellationToken)
        {
            Requests.Add([.. stepIndices]);
            if (!Deny)
                return await new SequenceRunner(isElevated: true).RunStepsAsync(sequence, stepIndices, sink, cancellationToken);

            var results = stepIndices.Select((_, k) => k == 0
                ? ProcedureResult.NeedsElevation("negado")
                : ProcedureResult.Skipped("Não executado.")).ToList();
            for (var k = 0; k < stepIndices.Count; k++)
                sink.Publish(new StepFinishedEvent(stepIndices[k], sequence.Steps[stepIndices[k]].Id, results[k]));
            return results;
        }
    }
}
