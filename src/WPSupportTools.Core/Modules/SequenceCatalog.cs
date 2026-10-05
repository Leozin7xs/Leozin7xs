using System.Diagnostics.CodeAnalysis;
using WPSupportTools.Core.Modules.Printing.Correction;
using WPSupportTools.Core.Procedures;

namespace WPSupportTools.Core.Modules;

/// <summary>
/// Lista fechada de sequências que a aplicação sabe executar. O processo elevado só aceita
/// identificadores presentes aqui — é isso que impede a execução de comandos arbitrários.
/// </summary>
public static class SequenceCatalog
{
    private static readonly IReadOnlyDictionary<string, Func<MaintenanceSequence>> Factories =
        new Dictionary<string, Func<MaintenanceSequence>>(StringComparer.Ordinal)
        {
            [PrintingFixSequence.Id] = PrintingFixSequence.Create,
        };

    public static IEnumerable<string> Ids => Factories.Keys;

    public static bool TryCreate(string id, [NotNullWhen(true)] out MaintenanceSequence? sequence)
    {
        sequence = Factories.TryGetValue(id, out var factory) ? factory() : null;
        return sequence is not null;
    }
}
