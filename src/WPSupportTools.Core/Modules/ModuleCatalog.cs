using WPSupportTools.Core.Modules.Printing;

namespace WPSupportTools.Core.Modules;

/// <summary>Módulos disponíveis na tela inicial. Novos módulos são registrados aqui.</summary>
public static class ModuleCatalog
{
    public static IReadOnlyList<SupportModule> All { get; } =
    [
        PrintingModule.Definition,
        // UniLab, Rede e Telefonia entrarão aqui quando forem implementados.
    ];
}
