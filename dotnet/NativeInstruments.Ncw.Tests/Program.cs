using NativeInstruments.Ncw;

var projectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
var fixtures = Path.Combine(projectRoot, "tests", "data");

Run("16-bit mono metadata and samples", () =>
{
    using var stream = File.OpenRead(Path.Combine(fixtures, "16-bit-mono.ncw"));
    var reader = NcwReader.Open(stream);
    var samples = reader.DecodeSamples();

    AssertEqual((ushort)1, reader.Header.Channels, "channel count");
    AssertEqual((ushort)16, reader.Header.BitsPerSample, "bits per sample");
    AssertEqual((int)reader.Header.SampleCount, samples.Length, "decoded sample count");
    AssertEqual(0, samples[0], "sample[0]");
    AssertEqual(0x001B, samples[16], "sample[16]");
    AssertEqual(0xFF5A, samples[32], "sample[32]");
});

Run("16-bit stereo sample count", () =>
{
    using var stream = File.OpenRead(Path.Combine(fixtures, "16-bit-stereo.ncw"));
    var reader = NcwReader.Open(stream);
    var samples = reader.DecodeSamples();

    AssertEqual(-1, samples[0], "left sample 0");
    AssertEqual(0, samples[1], "right sample 0");
    AssertEqual((int)reader.Header.SampleCount, samples.Length / reader.Header.Channels, "decoded frame count");
});

Run("onezero stereo sample count", () =>
{
    using var stream = File.OpenRead(Path.Combine(fixtures, "testfile-onezero-16-bit-stereo.ncw"));
    var reader = NcwReader.Open(stream);
    var samples = reader.DecodeSamples();

    AssertEqual((int)reader.Header.SampleCount, samples.Length / reader.Header.Channels, "decoded frame count");
});

Run("24-bit mono decodes", () =>
{
    using var stream = File.OpenRead(Path.Combine(fixtures, "24-bit-mono.ncw"));
    var reader = NcwReader.Open(stream);
    _ = reader.DecodeSamples();
});

Run("32-bit float flagged file decodes", () =>
{
    using var stream = File.OpenRead(Path.Combine(fixtures, "32-bit-mono-float.ncw"));
    var reader = NcwReader.Open(stream);
    _ = reader.DecodeSamples();
});

Run("unknown flags still decode", () =>
{
    using var stream = File.OpenRead(Path.Combine(fixtures, "unknown-flag.ncw"));
    var reader = NcwReader.Open(stream);
    _ = reader.DecodeSamples();
});

Console.WriteLine("All NCW decoder checks passed.");
return;

static void Run(string name, Action test)
{
    try
    {
        test();
        Console.WriteLine($"[PASS] {name}");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[FAIL] {name}");
        Console.Error.WriteLine(ex);
        Environment.Exit(1);
    }
}

static void AssertEqual<T>(T expected, T actual, string label)
    where T : IEquatable<T>
{
    if (!actual.Equals(expected))
    {
        throw new InvalidOperationException($"{label} expected {expected} but found {actual}.");
    }
}
