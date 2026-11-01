using System.Linq;
using WordNet.Model;

namespace WordNet.Internal;

internal static class ContentProfiles
{
    internal static bool ManagedFixedOutput(ConversionContext context, bool pdfOptionsForAllFormats = false) => !context.Typography.Enabled
        && (!pdfOptionsForAllFormats && context.Destination != DocumentFormat.Pdf || context.Options.Pdf.Accessibility == PdfAccessibility.None
            && context.Options.Pdf.Conformance == PdfConformance.Pdf17);

    // Each owner selects history and appearance policy explicitly.
    internal static bool OrdinaryParagraph(ParagraphBlock paragraph, bool textHistory = false, bool formatHistory = false,
        bool comments = false, bool emptyRuns = true, bool decorations = true, bool structuralHistory = false, bool inlineImagesAndLinks = false) =>
        paragraph.Control == null && paragraph.NestedControl == null && paragraph.TypedControlId == null && paragraph.List == null
        && (structuralHistory || paragraph.StructuralRevision == null)
        && (formatHistory || paragraph.ParagraphFormatRevision == null)
        && paragraph.Runs.All(r => (emptyRuns || r.Text.Length != 0) && (r.Image == null || inlineImagesAndLinks && r.Image.Anchor == null && !r.Image.Transformed) && r.Box == null && r.Note == null && r.Object == null
            && r.Control == null && r.TypedControlId == null && (r.Link == null || inlineImagesAndLinks) && r.FieldInstruction == null && r.Bookmark == null
            && (textHistory || r.Revision == null) && (formatHistory || r.FormatRevision == null) && (comments || r.Comment == null)
            && (decorations || !r.Appearance.Underline && !r.Appearance.Strike && r.Appearance.Background == null));
}
