using WPSupportTools.Core.Execution;
using WPSupportTools.Core.Modules;
using WPSupportTools.Core.Modules.Printing.Correction;
using WPSupportTools.Core.Platform;
using WPSupportTools.Core.Procedures;

namespace WPSupportTools.Core.Tests;

/// <summary>O fluxo completo "Corrigir problema de impressão", com a interface sem administrador.</summary>
public sealed class PrintingFlowTests
{
    private readonly FakePlatform _fake = new();
    private readonly FakeElevatedRunner _elevated = new();
    private readonly List<RunEvent> _events = [];

    public PrintingFlowTests() =>
        _fake.Queue.InstalledPrinters.UnionWith(["Samsung M408x Series", "ZDesigner TLP 2844"]);

    private void AddStuckJob() =>
        _fake.Jobs.Add(new PrintJobInfo("Samsung M408x Series", PrintJobStatus.Error, _fake.Now));

    private Task<SequenceReport> RunAsync() =>
        new SequenceRunner(isElevated: false, _elevated)
            .RunAsync(PrintingFixSequence.Create(_fake.Build()), new DelegateRunEventSink(_events.Add));

    private static ProcedureOutcome[] Outcomes(SequenceReport report) => report.Results.Select(r => r.Outcome).ToArray();

    // Índices: 0 verificar fila, 1 cancelar, 2 parar, 3 iniciar, 4 conferir, 5 verificar TSPrint, 6 abrir TSPrint.

    [Fact]
    public async Task Stuck_queue_cancels_jobs_and_restarts_spooler_with_a_single_elevation()
    {
        AddStuckJob();
        _fake.Jobs.Add(new PrintJobInfo("ZDesigner TLP 2844", PrintJobStatus.Printing, _fake.Now));

        var report = await RunAsync();

        Assert.Equal(SequenceOutcome.Succeeded, report.Outcome);
        Assert.Equal([[1, 2, 3]], _elevated.Requests);
        Assert.Equal(["Samsung M408x Series"], _fake.Queue.Cancelled);
        Assert.Equal(["stop Spooler", "start Spooler"], _fake.Services.Calls);
        Assert.Single(_fake.Jobs);   // a fila da etiquetadora não foi tocada
        Assert.Equal(
            [ProcedureOutcome.Success, ProcedureOutcome.Success, ProcedureOutcome.Success, ProcedureOutcome.Success,
             ProcedureOutcome.Success, ProcedureOutcome.NotNeeded, ProcedureOutcome.NotNeeded],
            Outcomes(report));
    }

    [Fact]
    public async Task Normal_queue_with_tsprint_open_changes_nothing_and_never_asks_for_admin()
    {
        _fake.Jobs.Add(new PrintJobInfo("Samsung M408x Series", PrintJobStatus.Printing, _fake.Now.AddMinutes(-5)));

        var report = await RunAsync();

        Assert.Equal(SequenceOutcome.Succeeded, report.Outcome);
        Assert.Empty(_elevated.Requests);
        Assert.Empty(_fake.Services.Calls);
        Assert.Empty(_fake.Queue.Cancelled);
        Assert.Empty(_fake.Processes.Started);
        Assert.Equal(ProcedureOutcome.NotNeeded, report.Results[6].Outcome);
    }

    [Fact]
    public async Task Normal_queue_with_tsprint_closed_tries_to_open_it()
    {
        _fake.Processes.TSPrintRunning = false;

        var report = await RunAsync();

        Assert.Empty(_elevated.Requests);
        // Caminho do TSPrint ainda não configurado: etapa simulada, nada aberto.
        Assert.Equal(SequenceOutcome.Simulated, report.Outcome);
        Assert.Equal(ProcedureOutcome.NotConfigured, report.Results[6].Outcome);
        Assert.Empty(_fake.Processes.Started);
    }

    [Fact]
    public async Task Denied_elevation_stops_the_flow_without_changes()
    {
        AddStuckJob();
        _elevated.Deny = true;

        var report = await RunAsync();

        Assert.Equal(SequenceOutcome.ElevationRequired, report.Outcome);
        Assert.Equal(1, report.FailedStepIndex);
        Assert.Empty(_fake.Services.Calls);
        Assert.Empty(_fake.Queue.Cancelled);
        Assert.Equal(ProcedureOutcome.Skipped, report.Results[4].Outcome);
    }

    [Fact]
    public async Task Failure_while_stopping_spooler_still_leaves_spooler_on()
    {
        AddStuckJob();
        _fake.Services.Spooler = FakeServices.Running() with { RunningDependents = ["Fax"] };

        var report = await RunAsync();

        Assert.Equal(SequenceOutcome.Failed, report.Outcome);
        Assert.Equal(2, report.FailedStepIndex);
        Assert.Equal(ProcedureOutcome.Success, report.Results[3].Outcome);
        Assert.Equal(ServiceState.Running, _fake.Services.Spooler!.State);
        Assert.Equal(ProcedureOutcome.Skipped, report.Results[4].Outcome);
    }

    [Fact]
    public async Task Spooler_is_turned_back_on_even_when_cancelling_fails()
    {
        _fake.Jobs.Add(new PrintJobInfo("Impressora removida", PrintJobStatus.Error, _fake.Now));

        var report = await RunAsync();

        Assert.Equal(SequenceOutcome.Failed, report.Outcome);
        Assert.Equal(1, report.FailedStepIndex);
        Assert.Equal(ProcedureOutcome.Skipped, report.Results[2].Outcome);
        Assert.Equal(ProcedureOutcome.Success, report.Results[3].Outcome);
        Assert.Equal(ServiceState.Running, _fake.Services.Spooler!.State);
    }

    [Fact]
    public async Task Job_still_stuck_after_restart_ends_in_failure_with_spooler_on()
    {
        AddStuckJob();
        _fake.Queue.JobsSurviveCancel = true;

        var report = await RunAsync();

        Assert.Equal(SequenceOutcome.Failed, report.Outcome);
        Assert.Equal(4, report.FailedStepIndex);
        Assert.Equal(["stop Spooler", "start Spooler"], _fake.Services.Calls);
        Assert.Equal(ServiceState.Running, _fake.Services.Spooler!.State);
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
