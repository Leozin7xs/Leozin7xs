using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using WPSupportTools.Core;
using WPSupportTools.Core.Logging;
using WPSupportTools.Core.Service;
using WPSupportTools.Core.Updates;

namespace WPSupportTools.App.Service;

/// <summary>
/// Atualização automática feita pelo serviço:
/// <list type="number">
///   <item>Consulta o latest.json da versão aprovada para as unidades (só leitura; nada é enviado).</item>
///   <item>Se for mais nova, confere a assinatura da WP antes de baixar.</item>
///   <item>Baixa o .exe e confere tamanho, SHA-256 e versão do arquivo.</item>
///   <item>Espera não haver manutenção em andamento, troca o arquivo e reinicia o serviço.</item>
/// </list>
/// Só funciona no programa instalado em Program Files e com a chave pública embutida pela compilação oficial.
/// </summary>
internal sealed class ServiceUpdater(IAppLog log, SemaphoreSlim maintenance)
{
    private const int MaxRedirects = 5;
    private bool _warnedNoKey;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(UpdateConstants.FirstCheckDelay, cancellationToken);
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await CheckOnceAsync(cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    log.Write(LogLevel.Warning, $"Atualização: {ex.GetType().Name}: {ex.Message}");
                }

                await Task.Delay(UpdateConstants.CheckInterval, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Serviço parando.
        }
    }

    /// <summary>Apaga o executável antigo deixado pela última atualização.</summary>
    public static void RemoveLeftovers(IAppLog log)
    {
        foreach (var leftover in new[] { ServiceConstants.InstalledExecutable + ".old", ServiceConstants.InstalledExecutable + ".new" })
        {
            try
            {
                if (File.Exists(leftover))
                    File.Delete(leftover);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                log.Write(LogLevel.Warning, $"Não foi possível apagar {Path.GetFileName(leftover)}: {ex.Message}");
            }
        }
    }

    private async Task CheckOnceAsync(CancellationToken cancellationToken)
    {
        var current = Environment.ProcessPath ?? "";
        if (!string.Equals(Path.GetFullPath(current), Path.GetFullPath(ServiceConstants.InstalledExecutable),
                StringComparison.OrdinalIgnoreCase))
            return;

        var publicKey = UpdateConstants.PublicKeyXml(typeof(Program).Assembly);
        if (publicKey is null)
        {
            if (!_warnedNoKey)
                log.Write(LogLevel.Warning, "Atualização automática desligada: esta versão não tem a chave de atualização.");
            _warnedNoKey = true;
            return;
        }

        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromMinutes(5),
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"WPSupportTools/{AppInfo.Version}");

        var manifestBytes = await DownloadAsync(http, UpdateConstants.ManifestUrl, 64 * 1024, cancellationToken);
        if (manifestBytes is null)
            return;   // ainda não há versão publicada para as unidades

        var manifest = UpdateManifest.TryParse(System.Text.Encoding.UTF8.GetString(manifestBytes));
        if (manifest is null)
        {
            log.Write(LogLevel.Warning, "Atualização: latest.json inválido; ignorado.");
            return;
        }

        if (!UpdateVerifier.IsNewer(manifest.Version, AppInfo.Version))
            return;

        if (!UpdateVerifier.SignatureIsValid(manifest, publicKey))
        {
            log.Write(LogLevel.Error, $"Atualização {manifest.Version} recusada: assinatura inválida.");
            return;
        }

        if (manifest.Size > UpdateConstants.MaxExecutableSize)
        {
            log.Write(LogLevel.Error, $"Atualização {manifest.Version} recusada: arquivo grande demais.");
            return;
        }

        log.Write(LogLevel.Info, $"Baixando atualização {AppInfo.Version} → {manifest.Version}.");
        var exe = await DownloadAsync(http, UpdateConstants.ExecutableUrl, manifest.Size, cancellationToken);
        if (exe is null || !UpdateVerifier.FileMatches(manifest, exe))
        {
            log.Write(LogLevel.Error, $"Atualização {manifest.Version} recusada: arquivo baixado não confere com o publicado.");
            return;
        }

        var newPath = current + ".new";
        var oldPath = current + ".old";
        await File.WriteAllBytesAsync(newPath, exe, cancellationToken);

        var fileVersion = FileVersionInfo.GetVersionInfo(newPath).FileVersion ?? "";
        if (!UpdateVerifier.SameVersion(fileVersion, manifest.Version))
        {
            File.Delete(newPath);
            log.Write(LogLevel.Error, $"Atualização recusada: o arquivo é da versão {fileVersion}, esperado {manifest.Version}.");
            return;
        }

        // Nunca troca no meio de uma manutenção. Não libera depois: o processo será reiniciado.
        await maintenance.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(oldPath))
                File.Delete(oldPath);
            File.Move(current, oldPath);      // permitido com o programa em execução
            File.Move(newPath, current);
        }
        catch
        {
            if (!File.Exists(current) && File.Exists(oldPath))
                File.Move(oldPath, current);
            maintenance.Release();
            throw;
        }

        log.Write(LogLevel.Info, $"Atualizado para a versão {manifest.Version}. Reiniciando o serviço.");

        // O Windows reinicia o serviço (configurado na instalação), já com o arquivo novo.
        Environment.Exit(1);
    }

    /// <summary>Baixa seguindo redirecionamentos só para endereços permitidos. Null se não existir (404).</summary>
    private static async Task<byte[]?> DownloadAsync(HttpClient http, string url, long maxBytes, CancellationToken ct)
    {
        var uri = new Uri(url);
        for (var redirect = 0; redirect <= MaxRedirects; redirect++)
        {
            if (!UpdateConstants.IsAllowedHost(uri))
                throw new InvalidOperationException($"Endereço não permitido: {uri.Host}");

            using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.StatusCode is HttpStatusCode.Moved or HttpStatusCode.Found or HttpStatusCode.SeeOther or
                HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            {
                uri = response.Headers.Location is { } location
                    ? (location.IsAbsoluteUri ? location : new Uri(uri, location))
                    : throw new InvalidOperationException("Redirecionamento sem destino.");
                continue;
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
                return null;
            response.EnsureSuccessStatusCode();

            if (response.Content.Headers.ContentLength > maxBytes)
                throw new InvalidOperationException("Arquivo maior que o esperado.");

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(chunk, ct)) > 0)
            {
                if (buffer.Length + read > maxBytes)
                    throw new InvalidOperationException("Arquivo maior que o esperado.");
                buffer.Write(chunk, 0, read);
            }

            return buffer.ToArray();
        }

        throw new InvalidOperationException("Redirecionamentos demais.");
    }
}
