using System;
using System.IO;

namespace WordNet.Internal;

// Inspect central and local flags without decompressing or executing any entry.
// ZIP64 and split archives are outside this bounded identity check.
internal static class ZipSecurity
{
    internal enum Result { Valid, Protected, Invalid, Unsupported }

    internal static Result Examine(Stream input, ConversionContext context)
    {
        long origin = input.Position;
        try
        {
            long length = input.Length - origin;
            if (length < 22 || U32(origin) != 0x04034b50) return Result.Invalid;
            long end = -1, earliest = Math.Max(origin, input.Length - 65557);
            for (long at = input.Length - 22; at >= earliest; at--)
            {
                if ((at & 511) == 0) context.CheckCancellation();
                if (U32(at) == 0x06054b50 && at + 22L + U16(at + 20) == input.Length) { end = at; break; }
            }
            if (end < 0 || U16(end + 4) != 0 || U16(end + 6) != 0) return Result.Invalid;
            int diskCount = U16(end + 8), count = U16(end + 10);
            long size = U32(end + 12), offset = U32(end + 16);
            if (count == 0xffff || diskCount == 0xffff || size == uint.MaxValue || offset == uint.MaxValue) return Result.Unsupported;
            if (diskCount != count) return Result.Invalid;
            if (count > context.Options.MaximumArchiveEntries) throw new InvalidDataException("Limit.ArchiveEntries: Too many entries for ZIP inspection.");
            if (offset > length || size > length - offset) return Result.Invalid;
            long directory = origin + offset, directoryEnd = directory + size;
            if (directoryEnd > end) return Result.Invalid;
            long position = directory; bool startsAtOrigin = false;
            for (int i = 0; i < count; i++)
            {
                context.CheckCancellation();
                if (position > directoryEnd - 46 || U32(position) != 0x02014b50) return Result.Invalid;
                int flags = U16(position + 8), name = U16(position + 28), extra = U16(position + 30), comment = U16(position + 32);
                long local = U32(position + 42), next = position + 46L + name + extra + comment;
                if (next > directoryEnd) return Result.Invalid;
                if ((flags & 0x41) != 0) return Result.Protected;
                int startDisk = U16(position + 34);
                if (startDisk != 0) return startDisk == 0xffff ? Result.Unsupported : Result.Invalid;
                if (local == uint.MaxValue) return Result.Unsupported;
                if (local > length - 30 || origin + local >= directory || U32(origin + local) != 0x04034b50) return Result.Invalid;
                if ((U16(origin + local + 6) & 0x41) != 0) return Result.Protected;
                startsAtOrigin |= local == 0;
                position = next;
            }
            return position != directoryEnd ? Result.Unsupported : startsAtOrigin ? Result.Valid : Result.Invalid;
        }
        catch (EndOfStreamException) { return Result.Invalid; }
        finally { input.Position = origin; }

        int U16(long offset)
        {
            if (offset < origin || offset > input.Length - 2) throw new EndOfStreamException();
            input.Position = offset; int a = input.ReadByte(), b = input.ReadByte();
            if (a < 0 || b < 0) throw new EndOfStreamException();
            return a | b << 8;
        }
        uint U32(long offset) => (uint)U16(offset) | (uint)U16(offset + 2) << 16;
    }
}
