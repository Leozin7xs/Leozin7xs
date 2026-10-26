namespace WPSupportTools.Core.Modules.Printing.Labels;

/// <summary>Padrão WP das etiquetadoras (ver docs/padrao-etiquetadoras.md).</summary>
public static class LabelConstants
{
    /// <summary>50,8 mm (2 pol), em décimos de milímetro.</summary>
    public const short StandardWidth = 508;

    /// <summary>25,4 mm (1 pol), em décimos de milímetro.</summary>
    public const short StandardLength = 254;

    /// <summary>Diferença tolerada ao comparar tamanhos (0,2 mm).</summary>
    public const short Tolerance = 2;
}

/// <summary>Informações que as etapas da etiquetadora deixam para as seguintes.</summary>
public static class LabelState
{
    public const string PrinterName = "labels.printer";
    public const string Language = "labels.language";
    public const string SizeNeedsFix = "labels.size-needs-fix";
}
