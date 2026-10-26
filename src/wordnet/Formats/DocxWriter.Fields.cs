using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using WordNet.Model;

namespace WordNet.Formats;

internal sealed partial class DocxWriter
{
    private IEnumerable<XElement> ComplexField(TextRun run, double? fallbackFontSize = null)
    {
        context.CheckCancellation();
        yield return Boundary("begin");
        string instruction = run.FieldInstruction!;
        int start = 0;
        foreach (var child in run.FieldChildren!.OrderBy(p => p.Key))
        {
            yield return Code(instruction.Substring(start, child.Key - start));
            if (child.Value.FieldChildren?.Count > 0)
                foreach (XElement element in ComplexField(child.Value, fallbackFontSize)) yield return element;
            else yield return Run(child.Value, fallbackFontSize);
            start = child.Key + 1;
        }
        yield return Code(instruction.Substring(start));
        yield return Boundary("separate");
        string result = model.FieldResults.TryGetValue(run, out string? value) ? value : run.Text;
        yield return Run(new TextRun(result, run.Bold, run.Italic, run.Code, run.Link, run.FontSize ?? fallbackFontSize, run.Color, appearance: run.Appearance));
        yield return Boundary("end");
    }
    private static XElement Boundary(string kind) => new XElement(W + "r", new XElement(W + "fldChar", new XAttribute(W + "fldCharType", kind)));
    private static XElement Code(string text) => new XElement(W + "r", new XElement(W + "instrText", new XAttribute(XNamespace.Xml + "space", "preserve"), text));
}
