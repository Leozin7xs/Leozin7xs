using System.Security.Cryptography;
using System.Text;
using WPSupportTools.Core.Updates;

namespace WPSupportTools.Core.Tests;

public sealed class UpdateTests : IDisposable
{
    private readonly RSA _key = RSA.Create(3072);

    public void Dispose() => _key.Dispose();

    private string PublicKey => _key.ToXmlString(false);

    /// <summary>Faz o mesmo que o workflow "Publicar para as unidades".</summary>
    private UpdateManifest Publish(string version, byte[] exe, RSA? signer = null)
    {
        var sha = Convert.ToHexString(SHA256.HashData(exe)).ToLowerInvariant();
        var signature = (signer ?? _key).SignData(Encoding.UTF8.GetBytes($"WPSupportTools|{version}|{sha}"),
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return new UpdateManifest(version, sha, exe.Length, Convert.ToBase64String(signature));
    }

    [Theory]
    [InlineData("0.5.0", "0.4.2", true)]
    [InlineData("0.5.1", "0.5.0", true)]
    [InlineData("1.0.0", "0.9.9", true)]
    [InlineData("0.5.0", "0.5.0", false)]
    [InlineData("0.4.2", "0.5.0", false)]   // nunca volta versão
    [InlineData("lixo", "0.5.0", false)]
    public void Only_newer_versions_are_installed(string published, string installed, bool expected) =>
        Assert.Equal(expected, UpdateVerifier.IsNewer(published, installed));

    [Theory]
    [InlineData("0.5.0", "0.5.0.0")]
    [InlineData("0.5.0+abc123", "0.5.0")]
    public void Version_formats_are_compared_correctly(string a, string b) =>
        Assert.True(UpdateVerifier.SameVersion(a, b));

    [Fact]
    public void Manifest_signed_with_the_wp_key_is_accepted()
    {
        var exe = Encoding.UTF8.GetBytes("programa novo");
        var manifest = Publish("0.5.0", exe);

        Assert.True(UpdateVerifier.SignatureIsValid(manifest, PublicKey));
        Assert.True(UpdateVerifier.FileMatches(manifest, exe));
    }

    [Fact]
    public void Manifest_signed_with_another_key_is_refused()
    {
        using var attacker = RSA.Create(3072);
        var manifest = Publish("0.5.0", "programa malicioso"u8.ToArray(), attacker);

        Assert.False(UpdateVerifier.SignatureIsValid(manifest, PublicKey));
    }

    [Fact]
    public void Changing_the_version_breaks_the_signature()
    {
        var manifest = Publish("0.5.0", "programa"u8.ToArray()) with { Version = "9.9.9" };

        Assert.False(UpdateVerifier.SignatureIsValid(manifest, PublicKey));
    }

    [Fact]
    public void Tampered_download_is_refused()
    {
        var manifest = Publish("0.5.0", "programa original"u8.ToArray());

        Assert.False(UpdateVerifier.FileMatches(manifest, "programa trocado!"u8.ToArray()));
    }

    [Fact]
    public void Garbage_manifest_is_ignored()
    {
        Assert.Null(UpdateManifest.TryParse("não é json"));
        Assert.Null(UpdateManifest.TryParse("{\"version\":\"0.5.0\"}"));
    }

    [Fact]
    public void Manifest_from_the_workflow_format_is_read()
    {
        var json = """{ "version": "0.5.0", "sha256": "%SHA%", "size": 10, "signature": "YWJj" }"""
            .Replace("%SHA%", new string('a', 64));

        var manifest = UpdateManifest.TryParse(json);

        Assert.NotNull(manifest);
        Assert.Equal("0.5.0", manifest.Version);
    }

    [Theory]
    [InlineData("https://github.com/Leozin7xs/Leozin7xs/releases/download/unidades/latest.json", true)]
    [InlineData("https://objects.githubusercontent.com/github-production-release-asset/x", true)]
    [InlineData("http://github.com/Leozin7xs/x", false)]                 // sem HTTPS
    [InlineData("https://github.com.site-falso.com/x", false)]
    [InlineData("https://exemplo.com/WPSupportTools.exe", false)]
    public void Only_github_addresses_are_followed(string url, bool expected) =>
        Assert.Equal(expected, UpdateConstants.IsAllowedHost(new Uri(url)));

    [Fact]
    public void Without_embedded_key_updates_are_disabled() =>
        Assert.Null(UpdateConstants.PublicKeyXml(typeof(UpdateTests).Assembly));
}
