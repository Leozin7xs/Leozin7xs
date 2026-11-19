using System.Buffers.Binary;
using System.Text;

namespace WPSupportTools.Core.Modules.Printing.Labels;

/// <summary>
/// Opções de impressão das Zebra guardadas na parte privada do DEVMODE (preferências de impressão), além do
/// tamanho. Só os layouts de driver mapeados são alterados; qualquer outro volta igual.
/// <list type="bullet">
///   <item><b>ZDesigner ZPL</b> (ex.: ZD220/ZD230, NiceLabel, bloco privado de 13482 bytes): "Opções de gráficos"
///         em Interpolação Clipart, Modo de transferência Impressão direta e Tipo de gráfico ZIP. O driver vem com
///         Interpolação em Foto. Com "Baixar" ele troca o tipo de gráfico para Padrão; por isso os três são gravados
///         juntos.</item>
///   <item><b>ZDesigner TLP 2844</b> (driver EPL antigo, bloco privado de 1533 bytes): Darkness 13, Dithering None e
///         "rotate 180°" marcado. Darkness e Dithering ficam numa área protegida por um CRC-16 (CCITT, polinômio
///         0x1021) que vai até o fim do DEVMODE; sem recalcular o CRC o driver descarta essas opções. O CRC é
///         recalculado pela diferença em relação ao DEVMODE lido do driver (CRC é linear), sem precisar saber onde
///         a área começa.</item>
/// </list>
/// Mapeamento detalhado em docs/padrao-etiquetadoras.md.
/// </summary>
public static class ZebraGraphics
{
    private enum Layout { Unknown, Zpl, Tlp2844 }

    /// <summary>Assinatura gravada pelos drivers ZDesigner (NiceLabel) no fim do bloco privado.</summary>
    private static readonly byte[] Signature = Encoding.ASCII.GetBytes("NICELbl");

    // ---- ZDesigner ZPL (mapeado no ZD220-203dpi ZPL) ----
    private const int ZplDriverExtra = 13482;
    /// <summary>Interpolação: 0 = Foto ... 5 = Clipart (posição absoluta no DEVMODE).</summary>
    private const int InterpolationOffset = 1888;
    /// <summary>Tipo de gráfico: 2 = ZIP, 11 = Padrão (outros valores a tela mostra como ZIP).</summary>
    private const int GraphicsTypeOffset = 1872;
    /// <summary>Modo de transferência de imagem: 0 = Impressão direta, 1 = Baixar.</summary>
    private const int TransferModeOffset = 2136;

    public const int Clipart = 5;
    public const int DirectPrint = 0;
    public const int Zip = 2;
    private const int GraphicsTypeDefault = 11;

    // ---- ZDesigner TLP 2844 (driver EPL antigo) ----
    private const int TlpDriverExtra = 1533;
    /// <summary>"rotate 180°": 1 byte, 0/1. Fica fora da área do CRC.</summary>
    private const int TlpRotateOffset = 230;
    /// <summary>CRC-16 da área protegida (uint32, só os 16 bits de baixo são usados).</summary>
    private const int TlpCrcOffset = 898;
    /// <summary>Darkness: 0 a 15 (uint32).</summary>
    private const int TlpDarknessOffset = 1252;
    /// <summary>Dithering: 0 = Smooth, 1 = Scanned, 2 = Detail, 3 = None (uint32).</summary>
    private const int TlpDitheringOffset = 1488;
    /// <summary>Nome do modelo gravado pelo driver (ASCII, terminado em zero).</summary>
    private const int TlpModelOffset = 1700;
    private static readonly byte[] TlpModel = Encoding.ASCII.GetBytes("ZDesigner TLP 2844\0");

    public const int TlpDarkness = 13;
    public const int TlpDitheringNone = 3;
    private static readonly string[] DitheringNames = ["Smooth", "Scanned", "Detail", "None"];

    /// <summary>O DEVMODE é de um driver mapeado e os valores estão dentro do esperado?</summary>
    public static bool IsKnownLayout(byte[] devMode) => LayoutOf(devMode) != Layout.Unknown;

    /// <summary>Já está no padrão WP? Layout desconhecido conta como "não dá para conferir" (true: nada a fazer).</summary>
    public static bool IsStandard(byte[] devMode) => LayoutOf(devMode) switch
    {
        Layout.Zpl => Read(devMode, InterpolationOffset) == Clipart && Read(devMode, TransferModeOffset) == DirectPrint &&
                      Read(devMode, GraphicsTypeOffset) == Zip,
        Layout.Tlp2844 => Read(devMode, TlpDarknessOffset) == TlpDarkness &&
                          Read(devMode, TlpDitheringOffset) == TlpDitheringNone && devMode[TlpRotateOffset] == 1,
        _ => true,
    };

    /// <summary>Cópia no padrão WP; layout desconhecido volta igual.</summary>
    public static byte[] WithStandard(byte[] devMode)
    {
        var copy = (byte[])devMode.Clone();
        switch (LayoutOf(copy))
        {
            case Layout.Zpl:
                BinaryPrimitives.WriteUInt32LittleEndian(copy.AsSpan(InterpolationOffset), Clipart);
                BinaryPrimitives.WriteUInt32LittleEndian(copy.AsSpan(TransferModeOffset), DirectPrint);
                BinaryPrimitives.WriteUInt32LittleEndian(copy.AsSpan(GraphicsTypeOffset), Zip);
                break;

            case Layout.Tlp2844:
                BinaryPrimitives.WriteUInt32LittleEndian(copy.AsSpan(TlpDarknessOffset), TlpDarkness);
                BinaryPrimitives.WriteUInt32LittleEndian(copy.AsSpan(TlpDitheringOffset), TlpDitheringNone);
                FixTlpCrc(devMode, copy);
                copy[TlpRotateOffset] = 1;
                break;
        }

        return copy;
    }

    public static string Describe(byte[] devMode)
    {
        switch (LayoutOf(devMode))
        {
            case Layout.Zpl:
                var interpolation = Read(devMode, InterpolationOffset);
                var transfer = Read(devMode, TransferModeOffset);
                var graphics = Read(devMode, GraphicsTypeOffset);
                return $"interpolação {(interpolation == Clipart ? "Clipart" : interpolation == 0 ? "Foto" : $"nível {interpolation}")}, " +
                       $"{(transfer == DirectPrint ? "impressão direta" : "baixar")}, " +
                       $"gráfico {(graphics == Zip ? "ZIP" : graphics == GraphicsTypeDefault ? "Padrão" : graphics.ToString())}";

            case Layout.Tlp2844:
                var dithering = Read(devMode, TlpDitheringOffset);
                return $"darkness {Read(devMode, TlpDarknessOffset)}, dithering {DitheringNames[dithering]}, " +
                       $"{(devMode[TlpRotateOffset] == 1 ? "rotação 180°" : "sem rotação 180°")}";

            default:
                return "gráficos: driver não reconhecido, não alterados";
        }
    }

    private static Layout LayoutOf(byte[] devMode)
    {
        if (devMode.Length < 220 || devMode.AsSpan(devMode.Length - Math.Min(64, devMode.Length)).IndexOf(Signature) < 0)
            return Layout.Unknown;

        var driverExtra = BinaryPrimitives.ReadUInt16LittleEndian(devMode.AsSpan(70));
        if (driverExtra == ZplDriverExtra && devMode.Length >= 220 + ZplDriverExtra &&
            Read(devMode, InterpolationOffset) <= Clipart && Read(devMode, TransferModeOffset) <= 1 &&
            Read(devMode, GraphicsTypeOffset) <= 32)
            return Layout.Zpl;

        if (driverExtra == TlpDriverExtra && devMode.Length == 220 + TlpDriverExtra &&
            devMode.AsSpan(TlpModelOffset).StartsWith(TlpModel) &&
            Read(devMode, TlpDarknessOffset) <= 15 && Read(devMode, TlpDitheringOffset) <= 3 &&
            devMode[TlpRotateOffset] <= 1 && Read(devMode, TlpCrcOffset) <= ushort.MaxValue)
            return Layout.Tlp2844;

        return Layout.Unknown;
    }

    /// <summary>
    /// Atualiza o CRC de <paramref name="changed"/> a partir do CRC (válido) de <paramref name="original"/>:
    /// CRC(novo) = CRC(original) XOR CRC0(diferença), com CRC0 = CRC-16 0x1021 começando em zero. Só vale para
    /// mudanças dentro da área do CRC (depois do próprio campo).
    /// </summary>
    private static void FixTlpCrc(byte[] original, byte[] changed)
    {
        var first = TlpDarknessOffset;   // primeira posição alterada dentro da área protegida
        var delta = new byte[original.Length - first];
        for (var i = 0; i < delta.Length; i++)
            delta[i] = (byte)(original[first + i] ^ changed[first + i]);

        var crc = Read(original, TlpCrcOffset) ^ Crc16Ccitt(delta);
        BinaryPrimitives.WriteUInt32LittleEndian(changed.AsSpan(TlpCrcOffset), crc);
    }

    internal static ushort Crc16Ccitt(ReadOnlySpan<byte> data, ushort crc = 0)
    {
        foreach (var b in data)
        {
            crc ^= (ushort)(b << 8);
            for (var bit = 0; bit < 8; bit++)
                crc = (crc & 0x8000) != 0 ? (ushort)((crc << 1) ^ 0x1021) : (ushort)(crc << 1);
        }

        return crc;
    }

    private static uint Read(byte[] devMode, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(devMode.AsSpan(offset));
}
