using System.Buffers.Binary;

namespace NativeInstruments.Ncw;

public sealed class NcwReader
{
    private const int HeaderSize = 120;
    private const int BlockHeaderSize = 16;
    private const int MaxSamplesPerBlock = 512;
    private static readonly ulong[] ValidMagicValues = [0x01A89ED631010000, 0x01A89ED630010000];

    private readonly Stream _stream;
    private readonly IReadOnlyList<uint> _blockOffsets;

    private NcwReader(Stream stream, NcwHeader header, IReadOnlyList<uint> blockOffsets)
    {
        _stream = stream;
        Header = header;
        _blockOffsets = blockOffsets;
    }

    public NcwHeader Header { get; }

    public static NcwReader Open(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanRead || !stream.CanSeek)
        {
            throw new ArgumentException("NCW decoding requires a readable, seekable stream.", nameof(stream));
        }

        stream.Seek(0, SeekOrigin.Begin);
        var header = ReadHeader(stream);
        var blockOffsets = ReadBlockOffsets(stream, header);
        return new NcwReader(stream, header, blockOffsets);
    }

    public int[] DecodeSamples()
    {
        var channelCount = (int)Header.Channels;
        if (channelCount == 0)
        {
            return [];
        }

        var totalSamples = checked((int)Header.SampleCount * channelCount);
        var overflowSamples = (totalSamples % MaxSamplesPerBlock) / channelCount;
        var channels = new List<int>[channelCount];

        for (var channelIndex = 0; channelIndex < channelCount; channelIndex++)
        {
            channels[channelIndex] = new List<int>((int)Header.SampleCount);
        }

        for (var blockIndex = 0; blockIndex < _blockOffsets.Count; blockIndex++)
        {
            var isFinalBlock = blockIndex == _blockOffsets.Count - 1;
            _stream.Seek((long)Header.DataOffset + _blockOffsets[blockIndex], SeekOrigin.Begin);

            for (var channelIndex = 0; channelIndex < channelCount; channelIndex++)
            {
                var blockHeader = ReadBlockHeader(_stream);
                var blockDataLength = GetBlockDataLength(blockHeader);
                var blockData = _stream.ReadExactBytes(blockDataLength);
                var decoded = DecodeBlock(blockHeader, blockData);

                for (var sampleIndex = 0; sampleIndex < decoded.Length; sampleIndex++)
                {
                    var isOverflowSample = overflowSamples > 0 && sampleIndex >= overflowSamples;
                    if (isFinalBlock && isOverflowSample)
                    {
                        continue;
                    }

                    channels[channelIndex].Add(decoded[sampleIndex]);
                }
            }
        }

        var decodedFrames = channels.Min(channel => channel.Count);
        var interleaved = new int[decodedFrames * channelCount];
        var writeIndex = 0;

        for (var sampleIndex = 0; sampleIndex < decodedFrames; sampleIndex++)
        {
            for (var channelIndex = 0; channelIndex < channelCount; channelIndex++)
            {
                interleaved[writeIndex++] = channels[channelIndex][sampleIndex];
            }
        }

        return interleaved;
    }

    private static NcwHeader ReadHeader(Stream stream)
    {
        using var headerStream = new MemoryStream(stream.ReadExactBytes(HeaderSize), writable: false);
        var magic = headerStream.ReadUInt64BigEndian();
        if (!ValidMagicValues.Contains(magic))
        {
            throw new NcwException($"Unexpected NCW file signature: 0x{magic:X16}.");
        }

        return new NcwHeader(
            headerStream.ReadUInt16LittleEndian(),
            headerStream.ReadUInt16LittleEndian(),
            headerStream.ReadUInt32LittleEndian(),
            headerStream.ReadUInt32LittleEndian(),
            headerStream.ReadUInt32LittleEndian(),
            headerStream.ReadUInt32LittleEndian(),
            headerStream.ReadUInt32LittleEndian());
    }

    private static IReadOnlyList<uint> ReadBlockOffsets(Stream stream, NcwHeader header)
    {
        var blockOffsetsLength = checked((int)(header.DataOffset - header.BlocksOffset));
        var numBlockEntries = blockOffsetsLength / sizeof(uint);
        var blockOffsets = new List<uint>(Math.Max(0, numBlockEntries - 1));

        for (var entryIndex = 1; entryIndex < numBlockEntries; entryIndex++)
        {
            blockOffsets.Add(stream.ReadUInt32LittleEndian());
        }

        return blockOffsets;
    }

    private static BlockHeader ReadBlockHeader(Stream stream)
    {
        using var blockHeaderStream = new MemoryStream(stream.ReadExactBytes(BlockHeaderSize), writable: false);
        var magic = blockHeaderStream.ReadUInt32BigEndian();
        if (magic != 0x160C9A3E)
        {
            throw new NcwException($"Unexpected NCW block signature: 0x{magic:X8}.");
        }

        return new BlockHeader(
            blockHeaderStream.ReadInt32LittleEndian(),
            blockHeaderStream.ReadInt16LittleEndian(),
            blockHeaderStream.ReadUInt16LittleEndian());
    }

    private int GetBlockDataLength(BlockHeader blockHeader)
    {
        if (blockHeader.Bits == 0)
        {
            var bytesPerSample = Header.BitsPerSample switch
            {
                24 => 3,
                _ => Header.BitsPerSample / 8
            };

            return checked(MaxSamplesPerBlock * bytesPerSample);
        }

        return Math.Abs(blockHeader.Bits) * 64;
    }

    private int[] DecodeBlock(BlockHeader blockHeader, byte[] blockData)
    {
        if (blockHeader.Bits > 0)
        {
            return DecodeDeltaBlock(blockHeader.BaseValue, blockData, blockHeader.Bits);
        }

        if (blockHeader.Bits < 0)
        {
            return DecodeTruncatedBlock(blockData, Math.Abs(blockHeader.Bits));
        }

        return DecodeRawBlock(blockData);
    }

    private static int[] DecodeDeltaBlock(int baseValue, byte[] deltas, int bits)
    {
        if (deltas.Length != bits * 64)
        {
            throw new NcwException("Invalid delta block size.");
        }

        var samples = new int[MaxSamplesPerBlock];
        var previous = baseValue;
        var deltaValues = ReadPackedSignedValues(deltas, bits);

        if (deltaValues.Length < MaxSamplesPerBlock)
        {
            throw new NcwException("Invalid packed delta block.");
        }

        for (var index = 0; index < samples.Length; index++)
        {
            samples[index] = previous;
            previous += deltaValues[index];
        }

        return samples;
    }

    private static int[] DecodeTruncatedBlock(byte[] data, int bitSize)
    {
        var values = new List<int>(MaxSamplesPerBlock);
        var bitOffset = 0;

        while (bitOffset + bitSize <= data.Length * 8)
        {
            var byteOffset = bitOffset / 8;
            var bitRemainder = bitOffset % 8;
            var bytesToRead = (bitSize + 7) / 8;

            var temp = 0;
            for (var index = 0; index < bytesToRead && byteOffset + index < data.Length; index++)
            {
                temp |= data[byteOffset + index] << (index * 8);
            }

            var value = bitSize >= 32
                ? temp >> bitRemainder
                : (temp >> bitRemainder) & ((1 << bitSize) - 1);
            values.Add(value);
            bitOffset += bitSize;
        }

        return values.ToArray();
    }

    private int[] DecodeRawBlock(byte[] data)
    {
        var bytesPerSample = Header.BitsPerSample switch
        {
            8 => 1,
            16 => 2,
            24 => 3,
            32 => 4,
            _ => throw new NcwException($"Unsupported raw sample size: {Header.BitsPerSample} bits.")
        };

        var expectedLength = MaxSamplesPerBlock * bytesPerSample;
        if (data.Length != expectedLength)
        {
            throw new NcwException("Invalid raw block size.");
        }

        var samples = new int[MaxSamplesPerBlock];

        for (var sampleIndex = 0; sampleIndex < MaxSamplesPerBlock; sampleIndex++)
        {
            var offset = sampleIndex * bytesPerSample;
            samples[sampleIndex] = bytesPerSample switch
            {
                1 => unchecked((sbyte)data[offset]),
                2 => BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(offset, bytesPerSample)),
                3 => ReadInt24LittleEndian(data.AsSpan(offset, bytesPerSample)),
                4 => BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset, bytesPerSample)),
                _ => throw new NcwException("Unsupported raw sample width.")
            };
        }

        return samples;
    }

    private static int ReadInt24LittleEndian(ReadOnlySpan<byte> bytes)
    {
        var value = bytes[0] | (bytes[1] << 8) | (bytes[2] << 16);
        if ((value & 0x0080_0000) != 0)
        {
            value |= unchecked((int)0xFF00_0000);
        }

        return value;
    }

    private static int[] ReadPackedSignedValues(byte[] data, int precisionInBits)
    {
        var values = new List<int>(MaxSamplesPerBlock);
        long accumulator = 0;
        var bitsInAccumulator = 0;
        var mask = precisionInBits >= 32 ? 0xFFFF_FFFFL : (1L << precisionInBits) - 1;

        foreach (var currentByte in data)
        {
            accumulator |= (long)currentByte << bitsInAccumulator;
            bitsInAccumulator += 8;

            while (bitsInAccumulator >= precisionInBits)
            {
                var value = (int)(accumulator & mask);
                if (precisionInBits < 32 && (value & (1 << (precisionInBits - 1))) != 0)
                {
                    value |= ~((1 << precisionInBits) - 1);
                }

                values.Add(value);
                accumulator >>= precisionInBits;
                bitsInAccumulator -= precisionInBits;
            }
        }

        return values.ToArray();
    }
}
