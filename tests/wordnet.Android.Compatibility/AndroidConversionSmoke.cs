using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace WordNet.Tests.Android;

// Small consumer example. MainActivity executes the complete shared suite on-device.
internal static class AndroidConversionSmoke
{
    internal static Task<ConversionResult> ConvertMarkdownAsync(Stream input, Stream output, CancellationToken cancellationToken)
    {
        var converter = new WordConverter();
        return converter.ConvertAsync(input, output, DocumentFormat.Markdown, DocumentFormat.Docx,
            new ConversionOptions(), cancellationToken);
    }
}
