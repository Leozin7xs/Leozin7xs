using System.Text.Json;
using WPSupportTools.Core.Execution;
using WPSupportTools.Core.Logging;
using WPSupportTools.Core.Procedures;
using WPSupportTools.Core.Telemetry;

namespace WPSupportTools.Core.Tests;

public sealed class UsageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wpst-uso-" + Guid.NewGuid().ToString("N"));
    private readonly MemoryLog _log = new();
    private readonly FakeTransport _transport = new();
    private DateTimeOffset _now = new(2026, 11, 4, 13, 0, 0, TimeSpan.Zero);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private string Queue => Path.Combine(_root, "fila");

    private UsageReporter Reporter() =>
        new(Queue, _transport, _log, "abc123", () => _now) { Unit = new UnitInfo("Widal", "Menino Deus") };

    private static SequenceReport Report(SequenceOutcome outcome, int? failed = null) =>
        new("printing.fix", outcome, [ProcedureResult.Ok("ok")], failed);

    // ---------- Unidade ----------

    [Theory]
    [InlineData("  menino   DEUS ", "Menino Deus")]
    [InlineData("santa mônica", "Santa Mônica")]
    [InlineData("SÃO JOÃO DA BARRA", "São João da Barra")]
    [InlineData("matriz", "Matriz")]
    public void Unit_name_is_standardized(string typed, string expected) =>
        Assert.Equal(expected, UnitInfo.NormalizeUnit(typed));

    [Fact]
    public void Unit_requires_a_known_laboratory_and_a_name()
    {
        Assert.Equal("Selecione o laboratório.", UnitInfo.TryCreate(null, "Matriz", out _));
        Assert.Equal("Selecione o laboratório.", UnitInfo.TryCreate("Outro", "Matriz", out _));
        Assert.Equal("Digite o nome da unidade.", UnitInfo.TryCreate("Widal", "  ", out _));
        Assert.Null(UnitInfo.TryCreate("widal", " medplex ", out var info));
        Assert.Equal(new UnitInfo("Widal", "Medplex"), info);
    }

    [Fact]
    public void Unit_is_saved_for_the_whole_computer_and_read_back()
    {
        var store = new UnitStore(Path.Combine(_root, "maquina", "unidade.json"), Path.Combine(_root, "usuario", "unidade.json"));
        Assert.Null(store.Load());

        var path = store.Save(new UnitInfo("Santa Helena", "Matriz"));

        Assert.Contains("maquina", path);
        Assert.Equal(new UnitInfo("Santa Helena", "Matriz"), store.Load());
    }

    [Fact]
    public void Unit_falls_back_to_the_user_folder_when_the_computer_folder_is_not_writable()
    {
        var blocked = Path.Combine(_root, "bloqueado");
        Directory.CreateDirectory(_root);
        File.WriteAllText(blocked, "é um arquivo, então não dá para criar a pasta");
        var store = new UnitStore(Path.Combine(blocked, "unidade.json"), Path.Combine(_root, "usuario", "unidade.json"));

        var path = store.Save(new UnitInfo("Widal", "Medplex"));

        Assert.Contains("usuario", path);
        Assert.Equal(new UnitInfo("Widal", "Medplex"), store.Load());
    }

    [Fact]
    public void Damaged_unit_file_asks_again()
    {
        var file = Path.Combine(_root, "maquina", "unidade.json");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "{ quebrado");

        Assert.Null(new UnitStore(file, Path.Combine(_root, "usuario", "unidade.json")).Load());
    }

    // ---------- Registros de uso ----------

    [Fact]
    public async Task Run_and_feedback_are_sent_with_the_same_attendance_id()
    {
        var reporter = Reporter();
        var runId = Guid.NewGuid();

        reporter.RunFinished(runId, Report(SequenceOutcome.Succeeded), null);
        _now = _now.AddSeconds(30);
        reporter.Feedback(runId, "printing.fix", resolved: true);
        await reporter.FlushAsync();

        Assert.Equal(2, _transport.Sent.Count);
        var run = JsonDocument.Parse(_transport.Sent[0]).RootElement;
        var feedback = JsonDocument.Parse(_transport.Sent[1]).RootElement;
        Assert.Equal("execucao", run.GetProperty("tipo").GetString());
        Assert.Equal("concluido", run.GetProperty("resultado").GetString());
        Assert.Equal("retorno", feedback.GetProperty("tipo").GetString());
        Assert.True(feedback.GetProperty("resolvido").GetBoolean());
        Assert.Equal(run.GetProperty("atendimento_id").GetString(), feedback.GetProperty("atendimento_id").GetString());
        Assert.Equal("Widal", run.GetProperty("laboratorio").GetString());
        Assert.Equal("Menino Deus", run.GetProperty("unidade").GetString());
        Assert.Equal("abc123", run.GetProperty("pc_id").GetString());
        Assert.Empty(reporter.PendingFiles());
    }

    [Fact]
    public async Task Failed_run_reports_the_failed_step()
    {
        var reporter = Reporter();
        reporter.RunFinished(Guid.NewGuid(), Report(SequenceOutcome.Failed, failed: 0), "labels.locate");
        await reporter.FlushAsync();

        var run = JsonDocument.Parse(_transport.Sent.Single()).RootElement;
        Assert.Equal("falhou", run.GetProperty("resultado").GetString());
        Assert.Equal("labels.locate", run.GetProperty("etapa_com_falha").GetString());
    }

    [Fact]
    public void Event_has_no_personal_or_computer_names()
    {
        var json = JsonSerializer.Serialize(new UsageEvent(Guid.NewGuid(), Guid.NewGuid(), UsageEvent.RunType, _now,
            "Widal", "Matriz", "abc123", "0.6.0", "printing.fix", "concluido", null, null));

        Assert.DoesNotContain(Environment.MachineName, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Environment.UserName, json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            ["id", "atendimento_id", "tipo", "ocorrido_em", "laboratorio", "unidade", "pc_id", "versao", "sequencia",
             "resultado", "etapa_com_falha", "resolvido"],
            JsonDocument.Parse(json).RootElement.EnumerateObject().Select(p => p.Name));
    }

    // ---------- Log do atendimento (só nas falhas) ----------

    private const string SampleLog =
        "[14:20:05] [Info] Unidade definida: Endocrimeta — Assis Brasil.\n" +
        "[14:20:11] [Info] Zebra instalada: 'ZDesigner ZD220-203dpi ZPL' (porta USB001, online).\n" +
        "[14:20:12] [Info] TSPrint: usuário '01451cendocri21'.\n" +
        "[14:20:13] [Info] UniLab: 1 processo(s) — C:\\Users\\maria.souza\\AppData\\Local\\Unilab\\Unilab.exe.\n" +
        "[14:20:14] [Info] Licença @01451fulano21 aplicada; Usuario=fulano.rdp no ini.\n" +
        "[14:20:35] [Error] Etapa 8/13 [printing.recheck-queue] Conferindo se a fila destravou: FALHA\n";

    [Fact]
    public async Task Failed_run_sends_its_log_without_license_users_or_windows_user_name()
    {
        var reporter = Reporter();
        reporter.RunFinished(Guid.NewGuid(), Report(SequenceOutcome.Failed, 0), "printing.recheck-queue", SampleLog);
        await reporter.FlushAsync();

        var log = JsonDocument.Parse(_transport.Sent.Single()).RootElement.GetProperty("log").GetString()!;
        Assert.Contains("ZDesigner ZD220-203dpi ZPL", log);
        Assert.Contains("printing.recheck-queue", log);
        Assert.Contains("[14:20:35]", log);
        Assert.DoesNotContain("01451cendocri21", log);
        Assert.DoesNotContain("fulano", log);
        Assert.DoesNotContain("maria.souza", log);
        Assert.Contains(@"C:\Users\***\AppData", log);
    }

    [Fact]
    public async Task Successful_run_never_sends_the_log()
    {
        var reporter = Reporter();
        reporter.RunFinished(Guid.NewGuid(), Report(SequenceOutcome.Succeeded), null, SampleLog);
        reporter.Feedback(Guid.NewGuid(), "printing.fix", resolved: true, SampleLog);
        await reporter.FlushAsync();

        Assert.All(_transport.Sent, json => Assert.False(JsonDocument.Parse(json).RootElement.TryGetProperty("log", out _)));
    }

    [Fact]
    public async Task Not_resolved_feedback_sends_the_log()
    {
        var reporter = Reporter();
        reporter.Feedback(Guid.NewGuid(), "printing.fix", resolved: false, SampleLog);
        await reporter.FlushAsync();

        Assert.Contains("ZD220", JsonDocument.Parse(_transport.Sent.Single()).RootElement.GetProperty("log").GetString());
    }

    [Fact]
    public async Task Panel_without_the_log_field_still_gets_the_record_without_the_log()
    {
        var reporter = Reporter();
        reporter.RunFinished(Guid.NewGuid(), Report(SequenceOutcome.Failed, 0), "labels.locate", SampleLog);
        _transport.Queue.Enqueue(SendResult.Rejected);   // banco ainda sem a coluna "log"

        await reporter.FlushAsync();

        var run = JsonDocument.Parse(_transport.Sent.Single()).RootElement;
        Assert.False(run.TryGetProperty("log", out _));
        Assert.Equal("labels.locate", run.GetProperty("etapa_com_falha").GetString());
        Assert.Empty(reporter.PendingFiles());
    }

    [Fact]
    public void Very_long_log_keeps_the_beginning_and_the_end()
    {
        var log = "INICIO\n" + new string('x', 50_000) + "\nFIM DA FALHA";

        var redacted = LogRedactor.Redact(log);

        Assert.True(redacted.Length <= LogRedactor.MaxLength);
        Assert.StartsWith("INICIO", redacted);
        Assert.EndsWith("FIM DA FALHA", redacted);
    }

    [Fact]
    public void Redaction_keeps_printer_names_ports_dates_and_job_numbers()
    {
        const string text = "ZDesigner ZD230-203dpi ZPL (2) porta USB001, IP_192.168.0.107, FP00012.SHD, wpst-20261117.log";
        Assert.Equal(text, LogRedactor.Redact(text));
    }

    [Fact]
    public void Run_log_writer_keeps_only_the_current_attendance()
    {
        var writer = new RunLogWriter(_log, () => new DateTime(2026, 11, 23, 9, 15, 0));
        var sequence = new MaintenanceSequence("printing.fix", "Corrigindo…", [FakeProcedure.Ok("teste")]);

        writer.RunStarted(sequence, elevatedProcess: false);
        writer.Handle(sequence, new LogEvent(LogLevel.Info, "primeiro atendimento"));
        writer.RunStarted(sequence, elevatedProcess: false);
        writer.Handle(sequence, new LogEvent(LogLevel.Warning, "segundo atendimento"));

        Assert.DoesNotContain("primeiro", writer.Transcript);
        Assert.Contains("[09:15:00] [Warning] segundo atendimento", writer.Transcript);
    }

    [Fact]
    public async Task Without_internet_records_stay_saved_and_go_later_in_order()
    {
        var reporter = Reporter();
        _transport.Next = SendResult.RetryLater;
        reporter.RunFinished(Guid.NewGuid(), Report(SequenceOutcome.Succeeded), null);
        _now = _now.AddMinutes(1);
        reporter.RunFinished(Guid.NewGuid(), Report(SequenceOutcome.Failed, 0), "printing.check-queue");
        await reporter.FlushAsync();

        Assert.Equal(2, reporter.PendingFiles().Count);
        Assert.Empty(_transport.Sent);

        _transport.Next = SendResult.Sent;
        await reporter.FlushAsync();

        Assert.Empty(reporter.PendingFiles());
        Assert.Equal(["concluido", "falhou"],
            _transport.Sent.Select(j => JsonDocument.Parse(j).RootElement.GetProperty("resultado").GetString()));
    }

    [Fact]
    public async Task Panel_not_configured_only_keeps_the_records()
    {
        _transport.Configured = false;
        var reporter = Reporter();
        reporter.RunFinished(Guid.NewGuid(), Report(SequenceOutcome.Succeeded), null);
        await reporter.FlushAsync();

        Assert.Single(reporter.PendingFiles());
        Assert.Equal(0, _transport.Attempts);
    }

    [Fact]
    public async Task Record_rejected_by_the_server_is_discarded_and_does_not_block_the_rest()
    {
        var reporter = Reporter();
        _transport.Configured = false;
        reporter.RunFinished(Guid.NewGuid(), Report(SequenceOutcome.Succeeded), null);
        _now = _now.AddMinutes(1);
        reporter.RunFinished(Guid.NewGuid(), Report(SequenceOutcome.Succeeded), null);

        _transport.Configured = true;
        _transport.Queue.Enqueue(SendResult.Rejected);
        await reporter.FlushAsync();

        Assert.Empty(reporter.PendingFiles());
        Assert.Single(_transport.Sent);
        Assert.Contains(_log.Lines, l => l.Contains("recusado"));
    }

    [Fact]
    public async Task Very_old_records_are_discarded()
    {
        _transport.Configured = false;
        var reporter = Reporter();
        reporter.RunFinished(Guid.NewGuid(), Report(SequenceOutcome.Succeeded), null);
        File.SetLastWriteTimeUtc(reporter.PendingFiles().Single(), _now.UtcDateTime.AddDays(-91));

        await reporter.FlushAsync();

        Assert.Empty(reporter.PendingFiles());
    }

    [Theory]
    [InlineData(SequenceOutcome.Succeeded, "concluido")]
    [InlineData(SequenceOutcome.Simulated, "simulado")]
    [InlineData(SequenceOutcome.Failed, "falhou")]
    [InlineData(SequenceOutcome.ElevationRequired, "sem_permissao")]
    public void Outcome_names_used_by_the_panel(SequenceOutcome outcome, string expected) =>
        Assert.Equal(expected, UsageReporter.OutcomeName(outcome));

    [Fact]
    public void Machine_id_is_stable_and_does_not_reveal_the_guid()
    {
        var id = MachineId.FromMachineGuid("6F9619FF-8B86-D011-B42D-00C04FC964FF");
        Assert.Equal(id, MachineId.FromMachineGuid(" 6f9619ff-8b86-d011-b42d-00c04fc964ff "));
        Assert.Equal(16, id.Length);
        Assert.DoesNotContain("6f9619ff", id);
    }

    [Fact]
    public void Real_transport_without_address_is_not_configured() =>
        Assert.False(new SupabaseUsageTransport(null, null).IsConfigured);

    private sealed class FakeTransport : IUsageTransport
    {
        public bool Configured { get; set; } = true;
        public SendResult Next { get; set; } = SendResult.Sent;
        public Queue<SendResult> Queue { get; } = new();
        public List<string> Sent { get; } = [];
        public int Attempts { get; private set; }

        public bool IsConfigured => Configured;

        public Task<SendResult> SendAsync(string json, CancellationToken cancellationToken)
        {
            Attempts++;
            var result = Queue.Count > 0 ? Queue.Dequeue() : Next;
            if (result == SendResult.Sent)
                Sent.Add(json);
            return Task.FromResult(result);
        }
    }

    private sealed class MemoryLog : IAppLog
    {
        public List<string> Lines { get; } = [];
        public string LogDirectory => "";
        public void Write(LogLevel level, string message) => Lines.Add(message);
    }
}
