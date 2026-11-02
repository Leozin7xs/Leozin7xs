using System.Reflection;

namespace WPSupportTools.Core.Updates;

/// <summary>De onde as unidades buscam atualizações (só leitura, nada é enviado).</summary>
public static class UpdateConstants
{
    /// <summary>Versão aprovada para as unidades (release "unidades" no GitHub).</summary>
    public const string ManifestUrl = "https://github.com/Leozin7xs/Leozin7xs/releases/download/unidades/latest.json";

    public const string ExecutableUrl = "https://github.com/Leozin7xs/Leozin7xs/releases/download/unidades/WPSupportTools.exe";

    /// <summary>Únicos endereços aceitos (o GitHub redireciona os downloads para githubusercontent.com).</summary>
    public static bool IsAllowedHost(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps &&
        (uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase));

    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);
    public static readonly TimeSpan FirstCheckDelay = TimeSpan.FromMinutes(1);
    public const long MaxExecutableSize = 200L * 1024 * 1024;

    /// <summary>
    /// Chave pública (XML RSA, em base64) embutida no .exe pela compilação no GitHub. Sem ela, a
    /// atualização automática fica desligada.
    /// </summary>
    public static string? PublicKeyXml(Assembly assembly)
    {
        var encoded = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "UpdatePublicKey")?.Value;
        if (string.IsNullOrWhiteSpace(encoded))
            return null;

        try
        {
            return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
