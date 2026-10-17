using System;
using System.IO;
using System.Reflection;

namespace WordNet.Internal;

internal static class PreviewBridge
{
    internal static byte[] Png(byte[] pdf, int width, int height, ConversionContext context)
    {
        Assembly provider;
        try { provider = Assembly.Load(new AssemblyName("wordnet.Ocr")); }
        catch (FileNotFoundException e) { throw new NotSupportedException("Preview.Provider: Reference matching wordnet.Ocr and native assets for PNG previews.", e); }
        MethodInfo entry = provider.GetType("WordNet.Ocr.PreviewRaster", true)!.GetMethod("Render", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Preview.Provider: The optional provider version is incompatible.");
        return ((Func<byte[], int, int, ConversionContext, byte[]>)Delegate.CreateDelegate(typeof(Func<byte[], int, int, ConversionContext, byte[]>), entry))(pdf, width, height, context);
    }
}
