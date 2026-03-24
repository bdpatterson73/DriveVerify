using System.Buffers.Binary;

namespace DriveVerify.Models;

public class TestBlockHeader
{
    public const int SerializedSize = 128;
    public const ulong MagicSignatureValue = 0x4654_4553_5448_4452;

    public ulong MagicSignature { get; set; } = MagicSignatureValue;
    public Guid SessionId { get; set; }
    public int FileIndex { get; set; }
    public int BlockIndex { get; set; }
    public long AbsoluteOffset { get; set; }
    public long TimestampUtc { get; set; }
    public int PayloadLength { get; set; }
    public uint PayloadChecksum { get; set; }

    public void Serialize(Span<byte> destination)
    {
        if (destination.Length < SerializedSize)
            throw new ArgumentException($"Destination must be at least {SerializedSize} bytes.");

        destination.Clear();
        int offset = 0;

        BinaryPrimitives.WriteUInt64LittleEndian(destination[offset..], MagicSignature);
        offset += 8;

        SessionId.TryWriteBytes(destination[offset..]);
        offset += 16;

        BinaryPrimitives.WriteInt32LittleEndian(destination[offset..], FileIndex);
        offset += 4;

        BinaryPrimitives.WriteInt32LittleEndian(destination[offset..], BlockIndex);
        offset += 4;

        BinaryPrimitives.WriteInt64LittleEndian(destination[offset..], AbsoluteOffset);
        offset += 8;

        BinaryPrimitives.WriteInt64LittleEndian(destination[offset..], TimestampUtc);
        offset += 8;

        BinaryPrimitives.WriteInt32LittleEndian(destination[offset..], PayloadLength);
        offset += 4;

        BinaryPrimitives.WriteUInt32LittleEndian(destination[offset..], PayloadChecksum);
        // offset += 4; remaining bytes are zero-padded (already cleared)
    }

    public static TestBlockHeader Deserialize(ReadOnlySpan<byte> source)
    {
        if (source.Length < SerializedSize)
            throw new ArgumentException($"Source must be at least {SerializedSize} bytes.");

        int offset = 0;
        var header = new TestBlockHeader();

        header.MagicSignature = BinaryPrimitives.ReadUInt64LittleEndian(source[offset..]);
        offset += 8;

        header.SessionId = new Guid(source.Slice(offset, 16));
        offset += 16;

        header.FileIndex = BinaryPrimitives.ReadInt32LittleEndian(source[offset..]);
        offset += 4;

        header.BlockIndex = BinaryPrimitives.ReadInt32LittleEndian(source[offset..]);
        offset += 4;

        header.AbsoluteOffset = BinaryPrimitives.ReadInt64LittleEndian(source[offset..]);
        offset += 8;

        header.TimestampUtc = BinaryPrimitives.ReadInt64LittleEndian(source[offset..]);
        offset += 8;

        header.PayloadLength = BinaryPrimitives.ReadInt32LittleEndian(source[offset..]);
        offset += 4;

        header.PayloadChecksum = BinaryPrimitives.ReadUInt32LittleEndian(source[offset..]);

        return header;
    }
}
