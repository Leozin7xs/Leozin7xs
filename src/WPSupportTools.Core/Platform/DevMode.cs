using System.Buffers.Binary;
using System.Text;

namespace WPSupportTools.Core.Platform;

/// <summary>
/// Leitura e alteração dos campos públicos de uma estrutura DEVMODE do Windows.
/// A parte privada do driver (depois de dmSize) é preservada byte a byte.
/// </summary>
public sealed class DevMode
{
    private const int DeviceNameOffset = 0;
    private const int SizeOffset = 68;
    private const int DriverExtraOffset = 70;
    private const int FieldsOffset = 72;
    private const int OrientationOffset = 76;
    private const int PaperSizeOffset = 78;
    private const int PaperLengthOffset = 80;
    private const int PaperWidthOffset = 82;

    public const int OrientationPortrait = 1;
    public const short PaperSizeUser = 256;

    [Flags]
    private enum Fields : uint
    {
        Orientation = 0x1,
        PaperSize = 0x2,
        PaperLength = 0x4,
        PaperWidth = 0x8,
    }

    private readonly byte[] _data;

    public DevMode(byte[] data)
    {
        if (data.Length < PaperWidthOffset + 2)
            throw new ArgumentException("DEVMODE muito pequeno.", nameof(data));
        _data = (byte[])data.Clone();
    }

    public byte[] ToArray() => (byte[])_data.Clone();

    public string DeviceName => Encoding.Unicode.GetString(_data, DeviceNameOffset, 64).TrimEnd('\0');

    public int PublicSize => BinaryPrimitives.ReadUInt16LittleEndian(_data.AsSpan(SizeOffset));

    public int DriverExtraSize => BinaryPrimitives.ReadUInt16LittleEndian(_data.AsSpan(DriverExtraOffset));

    public short Orientation => BinaryPrimitives.ReadInt16LittleEndian(_data.AsSpan(OrientationOffset));

    public short PaperSize => BinaryPrimitives.ReadInt16LittleEndian(_data.AsSpan(PaperSizeOffset));

    /// <summary>Altura do papel em décimos de milímetro.</summary>
    public short PaperLength => BinaryPrimitives.ReadInt16LittleEndian(_data.AsSpan(PaperLengthOffset));

    /// <summary>Largura do papel em décimos de milímetro.</summary>
    public short PaperWidth => BinaryPrimitives.ReadInt16LittleEndian(_data.AsSpan(PaperWidthOffset));

    /// <summary>Cópia com tamanho personalizado e orientação definidos (campos marcados em dmFields).</summary>
    public DevMode WithPaper(short widthTenthsMm, short lengthTenthsMm, short orientation)
    {
        var copy = new DevMode(_data);
        var span = copy._data.AsSpan();
        BinaryPrimitives.WriteInt16LittleEndian(span[OrientationOffset..], orientation);
        BinaryPrimitives.WriteInt16LittleEndian(span[PaperSizeOffset..], PaperSizeUser);
        BinaryPrimitives.WriteInt16LittleEndian(span[PaperLengthOffset..], lengthTenthsMm);
        BinaryPrimitives.WriteInt16LittleEndian(span[PaperWidthOffset..], widthTenthsMm);

        var fields = (Fields)BinaryPrimitives.ReadUInt32LittleEndian(span[FieldsOffset..]);
        fields |= Fields.Orientation | Fields.PaperSize | Fields.PaperLength | Fields.PaperWidth;
        BinaryPrimitives.WriteUInt32LittleEndian(span[FieldsOffset..], (uint)fields);
        return copy;
    }

    public string Describe() =>
        $"{PaperWidth / 10.0:0.0} × {PaperLength / 10.0:0.0} mm, {(Orientation == OrientationPortrait ? "retrato" : "paisagem")}";
}
