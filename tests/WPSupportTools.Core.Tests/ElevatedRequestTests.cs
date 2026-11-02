using WPSupportTools.Core.Execution;
using WPSupportTools.Core.Modules.Printing;

namespace WPSupportTools.Core.Tests;

public class ElevatedRequestTests
{
    private static string Json(string sequence, int[] steps, Dictionary<string, string>? state = null)
    {
        var st = state ?? new Dictionary<string, string>();
        return Modules.SequenceCatalog.TryCreate(sequence, out var seq) && steps.All(i => i >= 0 && i < seq.Steps.Count)
            ? ElevatedRequest.For(seq, steps, st).ToJson()
            : new ElevatedRequest(sequence, steps, steps.Select(_ => "?").ToList(), st).ToJson();
    }

    [Fact]
    public void Valid_request_for_admin_steps_is_accepted()
    {
        var json = Json("printing.fix", [1, 2, 3, 4, 5],
            new Dictionary<string, string> { [PrintingState.TargetPrinters] = "Brother DCP-T820DW Printer" });

        Assert.True(ElevatedRequest.TryParse(json, out var request, out var sequence, out _));
        Assert.Equal("printing.fix", sequence.Id);
        Assert.Equal([1, 2, 3, 4, 5], request.Steps);
    }

    [Theory]
    [InlineData("printing.desconhecida", new[] { 1 })]
    [InlineData("printing.fix", new[] { 0 })]        // verificação: não é de administrador
    [InlineData("printing.fix", new[] { 7 })]        // TSPrint: nunca como administrador
    [InlineData("printing.fix", new[] { 2, 1 })]     // fora de ordem
    [InlineData("printing.fix", new int[0])]
    public void Requests_outside_the_whitelist_are_refused(string sequence, int[] steps) =>
        Assert.False(ElevatedRequest.TryParse(Json(sequence, steps), out _, out _, out _));

    [Fact]
    public void Request_from_a_different_version_with_other_step_names_is_refused()
    {
        Assert.True(Modules.SequenceCatalog.TryCreate("printing.fix", out var sequence));
        var stale = new ElevatedRequest("printing.fix", [3], ["printing.start-spooler"], new Dictionary<string, string>());

        Assert.NotEqual("printing.start-spooler", sequence.Steps[3].Id);
        Assert.False(ElevatedRequest.TryParse(stale.ToJson(), out _, out _, out var error));
        Assert.Contains("versões diferentes", error);
    }

    [Fact]
    public void Unknown_state_keys_are_refused() =>
        Assert.False(ElevatedRequest.TryParse(
            Json("printing.fix", [1], new Dictionary<string, string> { ["comando"] = "format c:" }), out _, out _, out _));

    [Theory]
    [InlineData("")]
    [InlineData("não é json")]
    [InlineData("{\"SequenceId\":\"printing.fix\"}")]
    public void Malformed_requests_are_refused(string json) =>
        Assert.False(ElevatedRequest.TryParse(json, out _, out _, out var error) || error.Length == 0);
}
