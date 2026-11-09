using WPSupportTools.Core.Platform;
using WPSupportTools.Core.Procedures;

namespace WPSupportTools.Core.Modules.Unilab;

/// <summary>Botão "Instalar o UniLab": pede a licença, instala, preenche a licença e configura o TSPrint.</summary>
public static class UnilabModule
{
    public const string InstallId = "unilab.install";

    public static SupportModule Definition { get; } = new(
        Id: "unilab",
        DisplayName: "UniLab",
        Actions:
        [
            new SupportAction(
                Title: "Instalar o UniLab",
                Description: "Instala o UniLab novo, preenche a licença e configura o TSPrint.",
                Kind: SupportActionKind.Correcao,
                SequenceId: InstallId,
                IconGlyph: "",
                CompletionHint: "O UniLab foi aberto. Faça o login e teste uma impressão (A4 e etiqueta).",
                Input: new InputPrompt(
                    Question: "Qual licença vai ser utilizada neste PC?",
                    Help: "É o usuário do UniLab desta estação, com o @ na frente.",
                    Example: "Ex.: @01451exemplo01",
                    StateKey: UnilabState.License,
                    Normalize: UnilabCloud.NormalizeLicense)),
        ]);

    public static MaintenanceSequence CreateInstall() => CreateInstall(PlatformServices.Windows);

    internal static MaintenanceSequence CreateInstall(PlatformServices platform) => new(
        InstallId,
        "Instalando o UniLab…",
        [
            new CheckUnilabLicenseProcedure(),
            new CheckUnilabVersionProcedure(platform),
            new ConditionalProcedure(new InstallUnilabProcedure(platform), UnilabState.OnlyIfUpdateNeeded),
            new ConfigureUnilabProcedure(platform),
            new DisableLegacyTSPrintProcedure(platform),
            new OpenUnilabProcedure(platform),
        ],
        // O processo de administrador recebe só o nome oficial do instalador e a versão.
        transferableStateKeys: [UnilabState.InstallerFile, UnilabState.Version]);
}
