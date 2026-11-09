using System.Text.RegularExpressions;
using WPSupportTools.Core.Platform;

namespace WPSupportTools.Core.Modules.Unilab;

/// <summary>Instalador do UniLab publicado na página da Uniware.</summary>
public sealed record UnilabInstaller(string FileName, Version Version, bool Is64Bit)
{
    public Uri Url => new(UnilabRelease.Page, FileName);
}

/// <summary>
/// Regras do UniLab novo (Unilab Cloud): página oficial de downloads, escolha do instalador e conferência
/// do arquivo baixado. O instalador não tem assinatura digital, então o app só aceita o arquivo se vier da
/// página oficial (HTTPS), tiver o tamanho anunciado pelo servidor e os dados internos disserem
/// UNIWARE / Unilab na mesma versão do nome do arquivo.
/// </summary>
public static partial class UnilabRelease
{
    public static readonly Uri Page = new("https://uniware.com.br/updates/cloud/novo/");

    public const string ExpectedCompany = "UNIWARE";
    public const string ExpectedProduct = "Unilab";

    /// <summary>Menor tamanho aceitável (o instalador tem ~100 MB): evita instalar página de erro salva como .exe.</summary>
    public const long MinimumSize = 20L * 1024 * 1024;

    [GeneratedRegex("""href\s*=\s*["']?(Unilab-(\d+(?:\.\d+){1,3})-win(-x64)?\.exe)["'\s>]""", RegexOptions.IgnoreCase)]
    private static partial Regex InstallerLink();

    [GeneratedRegex(@"^Unilab(\s+\d[\d.]*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex UnilabName();

    /// <summary>Instaladores do Windows listados na página.</summary>
    public static IReadOnlyList<UnilabInstaller> Parse(string html) =>
        InstallerLink().Matches(html)
            .Select(m => new UnilabInstaller(m.Groups[1].Value, Version.Parse(m.Groups[2].Value), m.Groups[3].Success))
            .DistinctBy(i => i.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>A versão mais nova; no Windows 64 bits prefere o instalador x64, senão o universal.</summary>
    public static UnilabInstaller? Choose(IReadOnlyList<UnilabInstaller> installers, bool is64BitWindows)
    {
        var newest = installers.Select(i => i.Version).DefaultIfEmpty().Max();
        if (newest is null)
            return null;

        var candidates = installers.Where(i => i.Version == newest).ToList();
        return is64BitWindows
            ? candidates.FirstOrDefault(i => i.Is64Bit) ?? candidates.FirstOrDefault(i => !i.Is64Bit)
            : candidates.FirstOrDefault(i => !i.Is64Bit);
    }

    /// <summary>"Unilab" ou "Unilab 0.1.58" em Programas e Recursos (não confunde com UniSenha etc.).</summary>
    public static bool IsUnilabDisplayName(string? displayName) =>
        displayName is not null && UnilabName().IsMatch(displayName.Trim());

    /// <summary>A versão instalada já é a mais nova (ou mais nova que ela)?</summary>
    public static bool IsUpToDate(string? installed, Version latest) =>
        TryVersion(installed) is { } current && Normalize(current) >= Normalize(latest);

    /// <summary>Motivo para recusar o arquivo baixado, ou null se ele passou na conferência.</summary>
    public static string? Problem(ExecutableInfo file, long? announcedSize, UnilabInstaller installer)
    {
        if (file.Length < MinimumSize)
            return $"o arquivo tem só {file.Length / 1024} KB";
        if (announcedSize is { } size && size != file.Length)
            return $"o tamanho ({file.Length} bytes) é diferente do anunciado pelo site ({size} bytes)";
        if (!string.Equals(file.Company, ExpectedCompany, StringComparison.OrdinalIgnoreCase))
            return $"o fabricante é \"{file.Company ?? "(vazio)"}\", não {ExpectedCompany}";
        if (!string.Equals(file.Product, ExpectedProduct, StringComparison.OrdinalIgnoreCase))
            return $"o produto é \"{file.Product ?? "(vazio)"}\", não {ExpectedProduct}";
        if (TryVersion(file.ProductVersion) is not { } version || Normalize(version) != Normalize(installer.Version))
            return $"a versão interna ({file.ProductVersion ?? "vazia"}) não é a {installer.Version}";
        return null;
    }

    private static Version? TryVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var digits = Regex.Match(text, @"\d+(\.\d+){1,3}");
        return digits.Success && Version.TryParse(digits.Value, out var v) ? v : null;
    }

    private static Version Normalize(Version v) =>
        new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));
}
