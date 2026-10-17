using System;

namespace WordNet.Internal;

internal static class Guard
{
    internal static void Positive(int value, string name)
    {
#if NET8_0_OR_GREATER
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value, name);
#else
        if (value <= 0) throw new ArgumentOutOfRangeException(name);
#endif
    }
    internal static void NotNull(object? value, string name)
    {
#if NET8_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(value, name);
#else
        if (value == null) throw new ArgumentNullException(name);
#endif
    }
}
