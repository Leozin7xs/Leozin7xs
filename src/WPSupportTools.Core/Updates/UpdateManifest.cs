using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WPSupportTools.Core.Updates;

/// <summary>
/// Arquivo "latest.json" publicado pelo botão "Publicar para as unidades".
/// A assinatura cobre a versão e o SHA-256 do .exe; só a chave privada da WP (segredo do GitHub) assina.
/// </summary>
public sealed record UpdateManifest(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("sha256")] string Sha256,
    [property: JsonPropertyName("size")] long Size,
    [property: JsonPropertyName("signature")] string Signature)
{
    public static UpdateManifest? TryParse(string json)
    {
        try
        {
            var manifest = JsonSerializer.Deserialize<UpdateManifest>(json);
            return manifest is { Version.Length: > 0, Sha256.Length: 64, Signature.Length: > 0, Size: > 0 } ? manifest : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Texto exato que é assinado (o mesmo usado no workflow de publicação).</summary>
    public static string SignedText(string version, string sha256Hex) =>
        $"WPSupportTools|{version}|{sha256Hex.ToLowerInvariant()}";
}

public static class UpdateVerifier
{
    /// <summary>Verdadeiro só se a versão publicada for maior que a instalada.</summary>
    public static bool IsNewer(string published, string installed) =>
        TryParseVersion(published) is { } p && TryParseVersion(installed) is { } i && p > i;

    public static bool SameVersion(string a, string b) =>
        TryParseVersion(a) is { } x && TryParseVersion(b) is { } y && x == y;

    /// <summary>Confere a assinatura RSA (SHA-256, PKCS#1) com a chave pública embutida no programa.</summary>
    public static bool SignatureIsValid(UpdateManifest manifest, string publicKeyXml)
    {
        try
        {
            using var rsa = RSA.Create();
            rsa.FromXmlString(publicKeyXml);
            return rsa.VerifyData(
                Encoding.UTF8.GetBytes(UpdateManifest.SignedText(manifest.Version, manifest.Sha256)),
                Convert.FromBase64String(manifest.Signature),
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return false;
        }
    }

    /// <summary>Confere tamanho e SHA-256 do arquivo baixado contra o manifesto.</summary>
    public static bool FileMatches(UpdateManifest manifest, byte[] file) =>
        file.LongLength == manifest.Size &&
        string.Equals(Convert.ToHexString(SHA256.HashData(file)), manifest.Sha256, StringComparison.OrdinalIgnoreCase);

    private static Version? TryParseVersion(string text)
    {
        var plus = text.IndexOf('+');
        var clean = (plus >= 0 ? text[..plus] : text).Trim().TrimStart('v', 'V');
        if (!System.Version.TryParse(clean, out var version))
            return null;
        // 0.5.0 e 0.5.0.0 são a mesma versão.
        return new Version(version.Major, version.Minor, Math.Max(version.Build, 0), Math.Max(version.Revision, 0));
    }
}
