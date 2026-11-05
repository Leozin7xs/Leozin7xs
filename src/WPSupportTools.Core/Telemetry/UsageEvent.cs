using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace WPSupportTools.Core.Telemetry;

/// <summary>
/// Registro de um uso do app, enviado ao painel. Sem dados pessoais: nada de nome de usuário,
/// nome do computador, documentos ou mensagens de erro detalhadas.
/// <list type="bullet">
///   <item><c>tipo = "execucao"</c>: o app terminou uma sequência (resultado = concluido / falhou / sem_permissao / simulado).</item>
///   <item><c>tipo = "retorno"</c>: a funcionária respondeu "Funcionou" (resolvido = true) ou "Não funcionou" (false).</item>
/// </list>
/// Os dois registros do mesmo atendimento têm o mesmo <see cref="RunId"/>.
/// </summary>
public sealed record UsageEvent(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("atendimento_id")] Guid RunId,
    [property: JsonPropertyName("tipo")] string Type,
    [property: JsonPropertyName("ocorrido_em")] DateTimeOffset OccurredAt,
    [property: JsonPropertyName("laboratorio")] string? Laboratory,
    [property: JsonPropertyName("unidade")] string? Unit,
    [property: JsonPropertyName("pc_id")] string PcId,
    [property: JsonPropertyName("versao")] string AppVersion,
    [property: JsonPropertyName("sequencia")] string SequenceId,
    [property: JsonPropertyName("resultado")] string? Outcome,
    [property: JsonPropertyName("etapa_com_falha")] string? FailedStep,
    [property: JsonPropertyName("resolvido")] bool? Resolved)
{
    public const string RunType = "execucao";
    public const string FeedbackType = "retorno";
}

/// <summary>
/// Identificador anônimo do computador: resumo (SHA-256) do MachineGuid do Windows.
/// Serve só para contar quantos PCs usam o app em cada unidade; não dá para chegar ao nome do PC.
/// </summary>
public static class MachineId
{
    public static string Current { get; } = Compute();

    public static string FromMachineGuid(string machineGuid) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("wpst:" + machineGuid.Trim().ToLowerInvariant())))[..16];

    private static string Compute()
    {
        try
        {
            using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                .OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            if (key?.GetValue("MachineGuid") is string guid && !string.IsNullOrWhiteSpace(guid))
                return FromMachineGuid(guid);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
        }
        return "desconhecido";
    }
}
