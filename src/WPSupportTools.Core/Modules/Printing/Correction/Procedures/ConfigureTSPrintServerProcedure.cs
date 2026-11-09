using WPSupportTools.Core.Modules.Unilab;
using WPSupportTools.Core.Platform;
using WPSupportTools.Core.Procedures;

namespace WPSupportTools.Core.Modules.Printing.Correction.Procedures;

/// <summary>
/// Confere a conexão do TSPrint com o servidor de impressão (Configurações → Conexão) e, se estiver fora do
/// padrão WP (ex.: "Privado", URL de exemplo, usuário 00000Cent00), configura: servidor Compartilhado,
/// 20 processos, intervalo 2, URL, código ADM, porta, usuário RDP (do Unilab deste PC) e senha.
/// O TSPrint é fechado para gravar e reaberto depois. A senha nunca vai para o log.
/// </summary>
public sealed class ConfigureTSPrintServerProcedure(PlatformServices platform, string? executablePath, string? token)
    : IProcedure
{
    public ConfigureTSPrintServerProcedure(PlatformServices platform)
        : this(platform, PrintingConstants.TSPrintExecutablePath, TSPrintServerSettings.BuildToken())
    {
    }

    public string Id => "printing.tsprint-server";
    public string DisplayName => "Conferindo a conexão do TSPrint";
    public bool RequiresElevation => false;
    public TimeSpan Timeout => TSPrintPrinterSwitch.MaxDuration;

    public async Task<ProcedureResult> ExecuteAsync(ProcedureContext context, CancellationToken cancellationToken)
    {
        if (UnilabCloud.IsInstalled(platform))
            return await UnilabCloudAsync(context, cancellationToken);

        string? current;
        try
        {
            current = platform.TSPrintConfig.ReadGeneralConfig();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ProcedureResult.Fail("Não foi possível ler a configuração do TSPrint.", ex.Message);
        }

        if (current is null)
            return ProcedureResult.NotNeeded("TSPrint não encontrado neste computador.");

        // Usuário RDP: o que já está configurado vale; senão, o do Unilab deste PC.
        var configuredUser = TSPrintServerSettings.ConfiguredRdpUser(current);
        var unilabUser = configuredUser is null ? ReadUnilabUser(context) : null;
        var rdpUser = configuredUser ?? unilabUser;

        // Senha: se o TSPrint ainda não tem, usa a do app.
        var hasToken = TSPrintServerSettings.HasToken(current);
        var result = TSPrintServerSettings.Apply(current, unilabUser, hasToken ? null : token);
        if (result is null)
            return ProcedureResult.Fail("A configuração do TSPrint não está num formato reconhecido. Chame o suporte.");

        var missing = new List<string>();
        if (rdpUser is null)
            missing.Add("o usuário RDP não foi encontrado na pasta do Unilab (C:\\...Unilab..., linha \"Usuario=\")");
        if (!hasToken && token is null)
            missing.Add("esta versão do app não tem a senha do servidor");

        if (result.ChangedFields.Count == 0)
        {
            return missing.Count == 0
                ? ProcedureResult.Ok("O TSPrint já está conectado ao servidor compartilhado da WP.")
                : ProcedureResult.Fail($"O TSPrint não está completo: {string.Join("; ", missing)}. Chame o suporte.");
        }

        context.Log($"TSPrint: ajustando {string.Join(", ", result.ChangedFields.Select(Describe))}.");
        var applied = await new TSPrintPrinterSwitch(platform, executablePath).ApplyAsync(
            context, () => platform.TSPrintConfig.WriteGeneralConfig(result.Json),
            "TSPrint configurado no servidor compartilhado da WP", cancellationToken);

        if (!applied.IsSuccessful || missing.Count == 0)
            return applied;

        return ProcedureResult.Fail(
            $"TSPrint configurado em parte: {string.Join("; ", missing)}. Complete na tela do TSPrint ou chame o suporte.");
    }

    /// <summary>UniLab novo: o TSPrint é o embutido nele (config.json), derivado da licença.</summary>
    private async Task<ProcedureResult> UnilabCloudAsync(ProcedureContext context, CancellationToken cancellationToken)
    {
        string? current;
        try
        {
            current = platform.Unilab.ReadCloudConfig();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ProcedureResult.Fail("Não foi possível ler a configuração do UniLab.", ex.Message);
        }

        if (UnilabCloud.LicenseOf(current) is not { } license)
            return ProcedureResult.Fail("O UniLab novo deste PC está sem licença. Use o botão \"Instalar o UniLab\" para preencher.");

        var result = UnilabCloud.Apply(current, license, UnilabCloud.Password(license), []);
        if (result is null)
            return ProcedureResult.Fail("A configuração do UniLab não está num formato reconhecido. Chame o suporte.");
        if (result.Changes.Count == 0)
            return ProcedureResult.Ok($"O TSPrint do UniLab já está ligado e conectado ({license}).");

        context.Log($"UniLab: ajustando {string.Join(", ", result.Changes)}.");
        return await TSPrintPrinterSwitch.ForUnilabCloud(platform).ApplyAsync(
            context, () => platform.Unilab.WriteCloudConfig(result.Json),
            "TSPrint do UniLab ligado e conectado ao servidor da WP", cancellationToken);
    }


    private string? ReadUnilabUser(ProcedureContext context)
    {
        try
        {
            var user = platform.TSPrintConfig.ReadUnilabUser();
            context.Log(user is null
                ? "Unilab: usuário não encontrado nas pastas Unilab do disco C:."
                : $"Unilab: usuário '{user}'.");
            return user;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            context.Log($"Unilab: não foi possível procurar o usuário ({ex.Message}).");
            return null;
        }
    }

    private static string Describe(string field) => field switch
    {
        "servidor" => "servidor (Compartilhado)",
        "maxthreads" => $"processos ({TSPrintServerSettings.MaxProcesses})",
        "intervalo" => $"intervalo ({TSPrintServerSettings.IntervalSeconds} s)",
        "url" => "URL do serviço",
        "codigo_adm" => "código ADM",
        "porta" => $"porta ({TSPrintServerSettings.Port})",
        "rdp_user" => "usuário RDP",
        "token" => "senha",
        _ => field,
    };
}
