using WPSupportTools.Core.Execution;
using WPSupportTools.Core.Logging;
using WPSupportTools.Core.Procedures;

namespace WPSupportTools.Core.Tests;

public sealed class LoggingTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "wpst-tests-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTime Now = new(2026, 10, 5, 22, 41, 3);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private string ReadLog() => File.ReadAllText(Path.Combine(_dir, "wpst-20261005.log"));

    [Fact]
    public void Writes_timestamped_lines_to_daily_file()
    {
        var log = new FileAppLog(_dir, () => Now);

        log.Write(LogLevel.Info, "Início da correção de impressão");
        log.Write(LogLevel.Error, "linha\ncom quebra");

        var lines = ReadLog().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("[2026-10-05 22:41:03] [INFO ] Início da correção de impressão", lines[0]);
        Assert.Equal("[2026-10-05 22:41:03] [ERRO ] linha com quebra", lines[1]);
    }

    [Fact]
    public void Run_log_contains_version_steps_results_and_errors()
    {
        var writer = new RunLogWriter(new FileAppLog(_dir, () => Now));
        var sequence = new MaintenanceSequence("test.sequence", "Teste",
            [FakeProcedure.Ok("a"), FakeProcedure.Failing("b")]);

        writer.RunStarted(sequence, elevatedProcess: false);
        writer.Handle(sequence, new StepStartedEvent(0, "a"));
        writer.Handle(sequence, new StepFinishedEvent(0, "a", ProcedureResult.Ok("Serviço encontrado")));
        writer.Handle(sequence, new StepFinishedEvent(1, "b", ProcedureResult.Fail("falhou", "Win32Exception: acesso negado")));
        writer.RunFinished(sequence, new SequenceReport(sequence.Id, SequenceOutcome.Failed, [], 1));
        writer.UserFeedback(sequence.Id, resolved: false);

        var text = ReadLog();
        Assert.Contains($"v{AppInfo.Version}", text);
        Assert.Contains("Etapa 1/2 [a] Etapa a: OK — Serviço encontrado", text);
        Assert.Contains("Etapa 2/2 [b] Etapa b: FALHA — falhou | Erro: Win32Exception: acesso negado", text);
        Assert.Contains("etapa com falha: b", text);
        Assert.Contains("não funcionou", text);
        Assert.DoesNotContain(Environment.UserName, text);
    }
}
