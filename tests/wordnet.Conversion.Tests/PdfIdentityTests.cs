using System;
using System.Text;
using WordNet.Formats;
using Xunit;

namespace WordNet.Tests;

public sealed class PdfIdentityTests
{
    [Fact]
    public void NormalizationPreservesOffsetsAndUsesTheContentDigest()
    {
        byte[] bytes = Encoding.ASCII.GetBytes("prefix /ID [<aa12>\n<BB34>] end");
        PdfIdentity.Normalize(bytes);
        Assert.Equal("prefix /ID [<ED66>\n<ED66>] end", Encoding.ASCII.GetString(bytes));
    }

    [Fact]
    public void MalformedCandidatesAreSkippedAndMissingIdentifiersFail()
    {
        byte[] bytes = Encoding.ASCII.GetBytes("/ID[<badx><12>] /ID[<abc><DEF>]");
        PdfIdentity.Normalize(bytes);
        string output = Encoding.ASCII.GetString(bytes);
        Assert.StartsWith("/ID[<badx><12>] /ID[<", output, StringComparison.Ordinal);
        Assert.Equal(bytes.Length, output.Length); Assert.DoesNotContain("<abc><DEF>", output, StringComparison.Ordinal);
        byte[] missing = Encoding.ASCII.GetBytes(new string('x', 4096));
        Assert.Throws<InvalidOperationException>(() => PdfIdentity.Normalize(missing)); Assert.Equal(new string('x', 4096), Encoding.ASCII.GetString(missing));
    }
}
