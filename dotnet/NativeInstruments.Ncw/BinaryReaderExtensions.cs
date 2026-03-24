using System.Buffers.Binary;

namespace NativeInstruments.Ncw;

internal static class BinaryReaderExtensions
{
    public static byte[] ReadExactBytes(this Stream stream, int count)
    {
        var buffer = new byte[count];
        var offset = 0;

        while (offset < count)
        {
            var read = stream.Read(buffer, offset, count - offset);
            if (read == 0)
            {
                throw new NcwException($"Unexpected end of stream while reading {count} bytes.");
            }

            offset += read;
        }

        return buffer;
    }

    public static ushort ReadUInt16LittleEndian(this Stream stream) =>
        BinaryPrimitives.ReadUInt16LittleEndian(stream.ReadExactBytes(sizeof(ushort)));

    public static short ReadInt16LittleEndian(this Stream stream) =>
        BinaryPrimitives.ReadInt16LittleEndian(stream.ReadExactBytes(sizeof(short)));

    public static uint ReadUInt32LittleEndian(this Stream stream) =>
        BinaryPrimitives.ReadUInt32LittleEndian(stream.ReadExactBytes(sizeof(uint)));

    public static int ReadInt32LittleEndian(this Stream stream) =>
        BinaryPrimitives.ReadInt32LittleEndian(stream.ReadExactBytes(sizeof(int)));

    public static uint ReadUInt32BigEndian(this Stream stream) =>
        BinaryPrimitives.ReadUInt32BigEndian(stream.ReadExactBytes(sizeof(uint)));

    public static ulong ReadUInt64BigEndian(this Stream stream) =>
        BinaryPrimitives.ReadUInt64BigEndian(stream.ReadExactBytes(sizeof(ulong)));
}
