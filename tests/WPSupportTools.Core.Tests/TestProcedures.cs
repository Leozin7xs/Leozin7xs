using WPSupportTools.Core.Procedures;

namespace WPSupportTools.Core.Tests;

internal sealed class FakeProcedure(
    string id,
    Func<ProcedureContext, CancellationToken, Task<ProcedureResult>> body,
    bool requiresElevation = false,
    TimeSpan? timeout = null) : IProcedure
{
    public int Executions { get; private set; }

    public string Id => id;
    public string DisplayName => $"Etapa {id}";
    public bool RequiresElevation => requiresElevation;
    public TimeSpan Timeout => timeout ?? TimeSpan.FromSeconds(5);

    public Task<ProcedureResult> ExecuteAsync(ProcedureContext context, CancellationToken cancellationToken)
    {
        Executions++;
        return body(context, cancellationToken);
    }

    public static FakeProcedure Ok(string id, bool requiresElevation = false) =>
        new(id, (_, _) => Task.FromResult(ProcedureResult.Ok("ok")), requiresElevation);

    public static FakeProcedure Failing(string id) =>
        new(id, (_, _) => Task.FromResult(ProcedureResult.Fail("falhou", "detalhe")));

    public static FakeProcedure Throwing(string id, Exception ex) =>
        new(id, (_, _) => throw ex);
}
