namespace NativeInstruments.Ncw;

public enum ChannelEncoding
{
    LeftRight,
    MidSide
}

public enum SampleFormat
{
    Pcm,
    Float
}

public sealed record BlockHeader(int BaseValue, short Bits, ushort Flags)
{
    public ChannelEncoding ChannelEncoding =>
        (Flags & 0b0000_0000_0000_0001) != 0 ? ChannelEncoding.MidSide : ChannelEncoding.LeftRight;

    public SampleFormat SampleFormat =>
        (Flags & 0b0000_0000_0000_0010) != 0 ? SampleFormat.Float : SampleFormat.Pcm;
}
