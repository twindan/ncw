using NativeInstruments.Ncw;

if (args.Length < 2)
{
    Console.WriteLine("usage: ncw-convert <INPUT> <OUTPUT>");
    return;
}

using var input = File.OpenRead(args[0]);
var reader = NcwReader.Open(input);
var samples = reader.DecodeSamples();

using var output = File.Create(args[1]);
WavWriter.WritePcm(output, reader.Header, samples);

internal static class WavWriter
{
    public static void WritePcm(Stream output, NcwHeader header, IReadOnlyList<int> samples)
    {
        using var writer = new BinaryWriter(output, System.Text.Encoding.ASCII, leaveOpen: true);
        var bytesPerSample = header.BitsPerSample switch
        {
            8 => 1,
            16 => 2,
            24 => 3,
            32 => 4,
            _ => throw new NcwException($"Unsupported WAV bit depth: {header.BitsPerSample}.")
        };

        var blockAlign = checked((short)(header.Channels * bytesPerSample));
        var byteRate = checked((int)(header.SampleRate * blockAlign));
        var dataSize = checked(samples.Count * bytesPerSample);

        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + dataSize);
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)header.Channels);
        writer.Write((int)header.SampleRate);
        writer.Write(byteRate);
        writer.Write(blockAlign);
        writer.Write((short)header.BitsPerSample);
        writer.Write("data"u8.ToArray());
        writer.Write(dataSize);

        foreach (var sample in samples)
        {
            switch (header.BitsPerSample)
            {
                case 8:
                    writer.Write(unchecked((byte)(sbyte)sample));
                    break;
                case 16:
                    writer.Write(unchecked((short)sample));
                    break;
                case 24:
                    writer.Write((byte)(sample & 0xFF));
                    writer.Write((byte)((sample >> 8) & 0xFF));
                    writer.Write((byte)((sample >> 16) & 0xFF));
                    break;
                case 32:
                    writer.Write(sample);
                    break;
            }
        }
    }
}
