using WPSupportTools.Core.Modules.Printing;
using WPSupportTools.Core.Modules.Unilab;

namespace WPSupportTools.Core.Modules;

/// <summary>Módulos disponíveis na tela inicial. Novos módulos são registrados aqui.</summary>
public static class ModuleCatalog
{
    public static IReadOnlyList<SupportModule> All { get; } =
    [
        PrintingModule.Definition,
        UnilabModule.Definition,
        // Rede e Telefonia entrarão aqui quando forem implementados.
    ];
}
