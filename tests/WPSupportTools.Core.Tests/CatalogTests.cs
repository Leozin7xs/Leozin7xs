using WPSupportTools.Core.Modules;
using WPSupportTools.Core.Modules.Printing.Correction;
using WPSupportTools.Core.Procedures;

namespace WPSupportTools.Core.Tests;

public class CatalogTests
{
    [Fact]
    public void Printing_fix_sequence_is_registered()
    {
        Assert.True(SequenceCatalog.TryCreate(PrintingFixSequence.Id, out var sequence));
        Assert.Equal(6, sequence.Steps.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("printing.FIX")]
    [InlineData("powershell -c whoami")]
    public void Unknown_sequence_ids_are_rejected(string id)
    {
        Assert.False(SequenceCatalog.TryCreate(id, out _));
    }

    [Fact]
    public void Every_module_action_points_to_a_registered_sequence()
    {
        foreach (var action in ModuleCatalog.All.SelectMany(m => m.Actions))
            Assert.True(SequenceCatalog.TryCreate(action.SequenceId, out _), action.SequenceId);
    }

    [Fact]
    public void Procedure_ids_are_unique_across_all_sequences()
    {
        var ids = SequenceCatalog.Ids
            .SelectMany(id => SequenceCatalog.TryCreate(id, out var s) ? s.Steps : [])
            .Select(p => p.Id)
            .ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public async Task Placeholder_procedures_do_not_claim_success()
    {
        NotConfiguredProcedure.SimulatedDuration = TimeSpan.Zero;
        Assert.True(SequenceCatalog.TryCreate(PrintingFixSequence.Id, out var sequence));

        var report = await new Execution.SequenceRunner(isElevated: true)
            .RunAsync(sequence, new Execution.DelegateRunEventSink(_ => { }));

        Assert.Equal(Execution.SequenceOutcome.Simulated, report.Outcome);
        Assert.All(report.Results, r => Assert.Equal(ProcedureOutcome.NotConfigured, r.Outcome));
    }
}
