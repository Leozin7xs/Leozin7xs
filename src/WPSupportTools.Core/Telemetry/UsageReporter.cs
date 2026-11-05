using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using WPSupportTools.Core.Execution;
using WPSupportTools.Core.Logging;

namespace WPSupportTools.Core.Telemetry;

/// <summary>Para onde os registros vão. Enquanto <see cref="Endpoint"/> for null, eles só ficam guardados no PC.</summary>
public static class TelemetryConstants
{
    /// <summary>
    /// Tabela do Supabase que recebe os registros (projeto "wp-support-tools").
    /// Painel: https://wp-support-tools-painel.vercel.app. null = não envia, só guarda.
    /// Para trocar de conta/projeto: mudar estas duas linhas e publicar uma versão nova.
    /// </summary>
    public static Uri? Endpoint { get; } = new("https://uuupigoupfilyonkpung.supabase.co/rest/v1/usos");

    /// <summary>
    /// Chave pública (publishable) do Supabase. É pública por natureza: pelas regras do banco
    /// (RLS), ela só permite INSERIR registros em "usos" — não lê, não altera e não apaga nada.
    /// </summary>
    public static string? PublicKey => "sb_publishable_ssPgPPV_d8PaDkerNRmYpw_c32sh9qP";

    /// <summary>Registros guardados por mais tempo que isso, sem conseguir enviar, são descartados.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(90);

    /// <summary>Limite de registros guardados aguardando envio (os mais antigos saem primeiro).</summary>
    public const int MaxPending = 2000;

    public static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(10);
}

public enum SendResult
{
    Sent,
    /// <summary>Sem internet, servidor fora etc.: tenta de novo depois.</summary>
    RetryLater,
    /// <summary>O servidor recusou este registro (formato inválido): descarta para não travar a fila.</summary>
    Rejected,
}

public interface IUsageTransport
{
    bool IsConfigured { get; }
    Task<SendResult> SendAsync(string json, CancellationToken cancellationToken);
}

/// <summary>Envia para a tabela do Supabase (POST /rest/v1/&lt;tabela&gt;).</summary>
public sealed class SupabaseUsageTransport(Uri? endpoint, string? publicKey) : IUsageTransport
{
    private static readonly HttpClient Http = new() { Timeout = TelemetryConstants.SendTimeout };

    public bool IsConfigured => endpoint is not null && !string.IsNullOrWhiteSpace(publicKey) && endpoint.Scheme == Uri.UriSchemeHttps;

    public async Task<SendResult> SendAsync(string json, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
            return SendResult.RetryLater;

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("apikey", publicKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", publicKey);
        request.Headers.Add("Prefer", "return=minimal");

        try
        {
            using var response = await Http.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Conflict)   // 409 = já tinha sido enviado
                return SendResult.Sent;
            return response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity
                ? SendResult.Rejected
                : SendResult.RetryLater;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return SendResult.RetryLater;
        }
    }
}

/// <summary>
/// Guarda cada registro num arquivo (pasta do usuário) e envia quando houver internet e o painel
/// estiver configurado. Nunca atrapalha o atendimento: qualquer erro aqui só vai para o log.
/// </summary>
public sealed class UsageReporter
{
    private static readonly JsonSerializerOptions Json = new();

    private readonly string _queueDirectory;
    private readonly IUsageTransport _transport;
    private readonly IAppLog _log;
    private readonly Func<DateTimeOffset> _now;
    private readonly string _pcId;
    private readonly SemaphoreSlim _flushLock = new(1, 1);

    public UsageReporter(string queueDirectory, IUsageTransport transport, IAppLog log, string pcId, Func<DateTimeOffset>? now = null)
    {
        _queueDirectory = queueDirectory;
        _transport = transport;
        _log = log;
        _pcId = pcId;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public static string DefaultQueueDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WPSupportTools", "Uso", "Pendentes");

    public static UsageReporter CreateDefault(IAppLog log) => new(
        DefaultQueueDirectory,
        new SupabaseUsageTransport(TelemetryConstants.Endpoint, TelemetryConstants.PublicKey),
        log,
        MachineId.Current);

    /// <summary>Unidade atual (atualizada quando a funcionária preenche/altera).</summary>
    public UnitInfo? Unit { get; set; }

    public void RunFinished(Guid runId, SequenceReport report, string? failedStepId) =>
        Record(new UsageEvent(Guid.NewGuid(), runId, UsageEvent.RunType, _now(), Unit?.Laboratory, Unit?.Unit, _pcId,
            AppInfo.Version, report.SequenceId, OutcomeName(report.Outcome), failedStepId, null));

    public void Feedback(Guid runId, string sequenceId, bool resolved) =>
        Record(new UsageEvent(Guid.NewGuid(), runId, UsageEvent.FeedbackType, _now(), Unit?.Laboratory, Unit?.Unit, _pcId,
            AppInfo.Version, sequenceId, null, null, resolved));

    public static string OutcomeName(SequenceOutcome outcome) => outcome switch
    {
        SequenceOutcome.Succeeded => "concluido",
        SequenceOutcome.Simulated => "simulado",
        SequenceOutcome.ElevationRequired => "sem_permissao",
        _ => "falhou",
    };

    private void Record(UsageEvent usage)
    {
        try
        {
            Directory.CreateDirectory(_queueDirectory);
            // Nome começa pela data/hora: a ordem alfabética é a ordem de envio.
            var name = $"{usage.OccurredAt.UtcDateTime:yyyyMMddHHmmssfff}-{usage.Id:N}.json";
            File.WriteAllText(Path.Combine(_queueDirectory, name), JsonSerializer.Serialize(usage, Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Write(LogLevel.Warning, $"Painel: não foi possível guardar o registro de uso ({ex.Message}).");
            return;
        }

        _ = FlushAsync();
    }

    public IReadOnlyList<string> PendingFiles() =>
        Directory.Exists(_queueDirectory)
            ? Directory.GetFiles(_queueDirectory, "*.json").Order(StringComparer.Ordinal).ToList()
            : [];

    /// <summary>Envia o que estiver guardado, do mais antigo ao mais novo. Para no primeiro erro de rede.</summary>
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        await _flushLock.WaitAsync(cancellationToken);   // um envio por vez
        try
        {
            Prune();
            if (!_transport.IsConfigured)
                return;

            var sent = 0;
            foreach (var file in PendingFiles())
            {
                string json;
                try
                {
                    json = await File.ReadAllTextAsync(file, cancellationToken);
                }
                catch (IOException)
                {
                    continue;
                }

                var result = await _transport.SendAsync(json, cancellationToken);
                if (result == SendResult.RetryLater)
                    break;
                if (result == SendResult.Rejected)
                    _log.Write(LogLevel.Warning, $"Painel: registro recusado pelo servidor e descartado ({Path.GetFileName(file)}).");

                TryDelete(file);
                if (result == SendResult.Sent)
                    sent++;
            }

            if (sent > 0)
                _log.Write(LogLevel.Info, $"Painel: {sent} registro(s) de uso enviado(s).");
        }
        catch (Exception ex)
        {
            _log.Write(LogLevel.Warning, $"Painel: erro ao enviar registros de uso ({ex.GetType().Name}: {ex.Message}).");
        }
        finally
        {
            _flushLock.Release();
        }
    }

    /// <summary>Descarta registros antigos demais e mantém no máximo <see cref="TelemetryConstants.MaxPending"/>.</summary>
    private void Prune()
    {
        var files = PendingFiles();
        var limit = _now().UtcDateTime - TelemetryConstants.MaxAge;
        var expired = files.Where(f => File.GetLastWriteTimeUtc(f) < limit).ToList();
        var overflow = files.Except(expired).SkipLast(TelemetryConstants.MaxPending);
        foreach (var file in expired.Concat(overflow))
            TryDelete(file);
    }

    private static void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
