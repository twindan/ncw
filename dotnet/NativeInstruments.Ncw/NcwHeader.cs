namespace NativeInstruments.Ncw;

public sealed record NcwHeader(
    ushort Channels,
    ushort BitsPerSample,
    uint SampleRate,
    uint SampleCount,
    uint BlocksOffset,
    uint DataOffset,
    uint DataSize);
