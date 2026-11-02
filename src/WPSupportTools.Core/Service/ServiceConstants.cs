namespace WPSupportTools.Core.Service;

/// <summary>Nomes fixos do serviço do Windows instalado pelo técnico.</summary>
public static class ServiceConstants
{
    public const string ServiceName = "WPSupportToolsSvc";
    public const string DisplayName = "WP Support Tools";
    public const string Description =
        "Executa as correções de impressão do WP Support Tools sem pedir senha de administrador e mantém o programa atualizado.";

    /// <summary>Canal local por onde a interface pede ao serviço as etapas de administrador.</summary>
    public const string PipeName = "WPSupportTools.Service";

    public const string ExecutableName = "WPSupportTools.exe";

    public static string InstallDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WP Support Tools");

    public static string InstalledExecutable => Path.Combine(InstallDirectory, ExecutableName);

    /// <summary>Logs do serviço (roda como sistema, sem pasta de usuário).</summary>
    public static string ServiceLogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "WPSupportTools", "Logs");
}
