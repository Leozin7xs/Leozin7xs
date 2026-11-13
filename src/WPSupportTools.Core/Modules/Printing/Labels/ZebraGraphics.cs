using System.Buffers.Binary;
using System.Text;

namespace WPSupportTools.Core.Modules.Printing.Labels;

/// <summary>
/// "Opções de gráficos" do driver ZDesigner ZPL (NiceLabel), guardadas na parte privada do DEVMODE
/// (preferências de impressão). Padrão WP: Interpolação em Clipart, Modo de transferência em Impressão direta e
/// Tipo de gráfico ZIP.
/// O driver vem com Interpolação em Foto.
/// Com "Baixar" o driver troca o tipo de gráfico para Padrão; por isso os três são gravados juntos.
/// As posições foram mapeadas no ZDesigner ZD220-203dpi ZPL; outro layout de driver não é alterado.
/// </summary>
public static class ZebraGraphics
{
    /// <summary>Tamanho do bloco privado do layout mapeado.</summary>
    private const int KnownDriverExtra = 13482;

    /// <summary>Assinatura gravada pelo driver no fim do bloco privado.</summary>
    private static readonly byte[] Signature = Encoding.ASCII.GetBytes("NICELbl");

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

    /// <summary>O DEVMODE é do driver mapeado e os valores estão dentro do esperado?</summary>
    public static bool IsKnownLayout(byte[] devMode)
    {
        if (devMode.Length < 220 + KnownDriverExtra ||
            BinaryPrimitives.ReadUInt16LittleEndian(devMode.AsSpan(70)) != KnownDriverExtra ||
            devMode.AsSpan(devMode.Length - 64).IndexOf(Signature) < 0)
            return false;
        var interpolation = Read(devMode, InterpolationOffset);
        var transfer = Read(devMode, TransferModeOffset);
        return interpolation <= Clipart && transfer <= 1 && Read(devMode, GraphicsTypeOffset) <= 32;
    }

    /// <summary>Já está no padrão WP? Layout desconhecido conta como "não dá para conferir" (true: nada a fazer).</summary>
    public static bool IsStandard(byte[] devMode) =>
        !IsKnownLayout(devMode) ||
        (Read(devMode, InterpolationOffset) == Clipart && Read(devMode, TransferModeOffset) == DirectPrint &&
         Read(devMode, GraphicsTypeOffset) == Zip);

    /// <summary>Cópia no padrão WP; layout desconhecido volta igual.</summary>
    public static byte[] WithStandard(byte[] devMode)
    {
        var copy = (byte[])devMode.Clone();
        if (!IsKnownLayout(copy))
            return copy;
        BinaryPrimitives.WriteUInt32LittleEndian(copy.AsSpan(InterpolationOffset), Clipart);
        BinaryPrimitives.WriteUInt32LittleEndian(copy.AsSpan(TransferModeOffset), DirectPrint);
        BinaryPrimitives.WriteUInt32LittleEndian(copy.AsSpan(GraphicsTypeOffset), Zip);
        return copy;
    }

    public static string Describe(byte[] devMode)
    {
        if (!IsKnownLayout(devMode))
            return "gráficos: driver não reconhecido, não alterados";
        var interpolation = Read(devMode, InterpolationOffset);
        var transfer = Read(devMode, TransferModeOffset);
        var graphics = Read(devMode, GraphicsTypeOffset);
        return $"interpolação {(interpolation == Clipart ? "Clipart" : interpolation == 0 ? "Foto" : $"nível {interpolation}")}, " +
               $"{(transfer == DirectPrint ? "impressão direta" : "baixar")}, " +
               $"gráfico {(graphics == Zip ? "ZIP" : graphics == GraphicsTypeDefault ? "Padrão" : graphics.ToString())}";
    }

    private static uint Read(byte[] devMode, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(devMode.AsSpan(offset));
}
