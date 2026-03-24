namespace NativeInstruments.Ncw;

public sealed class NcwException : Exception
{
    public NcwException(string message)
        : base(message)
    {
    }

    public NcwException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
