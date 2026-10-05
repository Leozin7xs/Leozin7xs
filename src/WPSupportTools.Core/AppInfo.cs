using System.Reflection;

namespace WPSupportTools.Core;

public static class AppInfo
{
    public const string Name = "WP Support Tools";

    /// <summary>Versão definida em Directory.Build.props (ex.: "0.1.0").</summary>
    public static string Version { get; } = ReadVersion();

    private static string ReadVersion()
    {
        var informational = typeof(AppInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (string.IsNullOrWhiteSpace(informational))
            return typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

        // Remove o sufixo "+<hash do commit>" que o SDK adiciona automaticamente.
        var plus = informational.IndexOf('+');
        return plus >= 0 ? informational[..plus] : informational;
    }
}
