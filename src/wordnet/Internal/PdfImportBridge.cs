using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using WordNet.Model;

[assembly: InternalsVisibleTo("wordnet.Ocr")]
[assembly: InternalsVisibleTo("wordnet.Conversion.Tests")]
[assembly: InternalsVisibleTo("wordnet.Android.Compatibility")]

namespace WordNet.Internal;

internal static class PdfImportBridge
{
    internal static DocumentModel Read(Stream input, ConversionContext context)
    {
        Assembly provider;
        try { provider = Assembly.Load(new AssemblyName("wordnet.Ocr")); }
        catch (FileNotFoundException error)
        { throw new NotSupportedException("Pdf.ImportProvider: Reference the optional wordnet.Ocr NuGet package for PDF import and OCR.", error); }
        Type entry = provider.GetType("WordNet.Ocr.PdfImport", throwOnError: true)!;
        MethodInfo method = entry.GetMethod("Read", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Pdf.ImportProvider: The installed OCR provider is incompatible with this wordnet version.");
        var read = (Func<Stream, ConversionContext, DocumentModel>)Delegate.CreateDelegate(typeof(Func<Stream, ConversionContext, DocumentModel>), method);
        return read(input, context);
    }
}
