using System.IO;
#if NET8_0_OR_GREATER
using System;
#endif

namespace WordNet.Internal;

internal sealed class BoundedMemoryStream : MemoryStream
{
    private readonly long maximumBytes;

    internal BoundedMemoryStream(long maximumBytes)
    {
        this.maximumBytes = maximumBytes;
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        CheckLength(Position + count);
        base.Write(buffer, offset, count);
    }

    public override void WriteByte(byte value)
    {
        CheckLength(Position + 1);
        base.WriteByte(value);
    }

#if NET8_0_OR_GREATER
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        CheckLength(Position + buffer.Length);
        base.Write(buffer);
    }
#endif

    public override void SetLength(long value)
    {
        CheckLength(value);
        base.SetLength(value);
    }

    private void CheckLength(long value)
    {
        if (value > maximumBytes)
        {
            throw new InvalidDataException("Limit.Bytes: Conversion exceeds the configured byte limit.");
        }
    }
}
