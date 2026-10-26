using System.Text;

namespace WPSupportTools.Core.Modules.Printing.Labels;

public enum ZebraLanguage
{
    Epl,
    Zpl,
}

/// <summary>Identifica a linguagem da etiquetadora pelo nome do driver e fornece os comandos fixos.</summary>
public static class ZebraModel
{
    /// <summary>Null se não for um driver ZDesigner reconhecido.</summary>
    public static ZebraLanguage? LanguageOf(string driverName)
    {
        if (!driverName.Contains("ZDesigner", StringComparison.OrdinalIgnoreCase))
            return null;

        // Ex.: "ZDesigner GC420t (EPL)", "ZDesigner TLP 2844" → EPL.
        if (driverName.Contains("EPL", StringComparison.OrdinalIgnoreCase) ||
            driverName.Contains("TLP 2844", StringComparison.OrdinalIgnoreCase) ||
            driverName.Contains("TLP2844", StringComparison.OrdinalIgnoreCase))
            return ZebraLanguage.Epl;

        // Ex.: "ZDesigner ZD230-203dpi ZPL", "ZDesigner ZD220-203dpi ZPL", "ZDesigner GC420t".
        return ZebraLanguage.Zpl;
    }

    /// <summary>
    /// Comando de calibração (o mesmo efeito do botão "Calibrate" do driver):
    /// ZPL "~JC" (calibração do sensor de mídia); EPL "xa" (AutoSense).
    /// </summary>
    public static byte[] CalibrationCommand(ZebraLanguage language) => Ascii(language switch
    {
        ZebraLanguage.Zpl => "~JC\r\n",
        _ => "\r\nxa\r\n",
    });

    /// <summary>Etiqueta de teste 2 × 1 pol enviada direto à impressora (não depende do driver).</summary>
    public static byte[] TestLabel(ZebraLanguage language) => Ascii(language switch
    {
        ZebraLanguage.Zpl =>
            "^XA\r\n" +
            "^FO20,15^A0N,30,30^FDWP Support Tools^FS\r\n" +
            "^FO20,50^A0N,24,24^FDEtiqueta de teste^FS\r\n" +
            "^FO20,85^BY2^BCN,60,Y,N,N^FD123456789^FS\r\n" +
            "^XZ\r\n",
        _ =>
            "\r\nN\r\n" +
            "A20,15,0,3,1,1,N,\"WP Support Tools\"\r\n" +
            "A20,50,0,2,1,1,N,\"Etiqueta de teste\"\r\n" +
            "B20,85,0,1,2,4,60,B,\"123456789\"\r\n" +
            "P1\r\n",
    });

    private static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);
}
