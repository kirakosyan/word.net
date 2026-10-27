using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using WordNet.Internal;
using WordNet.Model;

namespace WordNet;

public sealed partial class WordDocument
{
    /// <summary>Replaces a dedicated body bookmark paragraph with a private copy of an admitted document fragment.</summary>
    /// <remarks>Load caller-supplied DOCX or safe HTML into a separate session first. The placeholder must
    /// occupy one whole body paragraph. Source appearance is retained; section geometry and stories are
    /// not imported. Both sessions remain unchanged if validation, cancellation or admission fails.</remarks>
    public DocumentEditResult InsertFragmentAtBookmark(string name, WordDocument fragment, CancellationToken cancellationToken = default)
    {
        ValidateBookmarkName(name); Guard.NotNull(fragment, nameof(fragment));
        if (ReferenceEquals(fragment, this)) throw new ArgumentException("Fragment.Source: A fragment must be a separate document session.", nameof(fragment));
        DocumentModel incoming;
        lock (fragment.gate)
        {
            if (fragment.diagnostics.Any(d => d.Severity == DiagnosticSeverity.Warning))
                throw new NotSupportedException("Fragment.SourceLoss: Resolve source load losses before inserting a fragment.");
            if (fragment.model.Controls.HasControls || fragment.model.Review.HasHistory || fragment.model.ObjectCount != 0)
                throw new NotSupportedException("Fragment.SourceStructure: Typed controls, history and retained objects are unsupported in fragments.");
            var context = new ConversionContext(options, cancellationToken);
            Admit(fragment.model, context); incoming = new DocumentCloner(context).Copy(fragment.model);
        }
        return Edit((m, c) =>
        {
            if (incoming.Blocks.Count == 0) throw new InvalidDataException("Fragment.Empty: A fragment must contain at least one body block.");
            PageSettings[] sections = incoming.Blocks.Select(b => b.Section).Concat(new[] { incoming.FinalSection }).Where(s => s != null).Select(s => s!).Distinct().ToArray();
            if (sections.Length > 1 || sections.Any(s => s.Stories.Values.Any(story => story.Count != 0)))
                throw new NotSupportedException("Fragment.Section: Fragment section breaks and stories are unsupported.");
            if (incoming.Tables.Any(table => table.Rows.SelectMany(row => row).SelectMany(cell => cell.Blocks).Any(block => block is TableBlock)))
                throw new NotSupportedException("Fragment.Table: Nested fragment tables are unsupported.");
            TextRun[] sourceRuns = incoming.Paragraphs.SelectMany(p => p.Runs).ToArray();
            if (sourceRuns.Any(r => r.Box != null || r.Object != null || r.FieldInstruction != null))
                throw new NotSupportedException("Fragment.Structure: Positioned objects and fields are unsupported in fragments.");
            ValidateRanges(incoming);
            var names = new HashSet<string>(sourceRuns.Where(r => r.Bookmark != null && !Starts(r.Bookmark, '/')).Select(r => r.Bookmark!), StringComparer.Ordinal);
            if (sourceRuns.Any(r => r.Link != null && Starts(r.Link, '#') && !names.Contains(r.Link.Substring(1))))
                throw new NotSupportedException("Fragment.Reference: Fragment links must resolve within the fragment.");
            var matches = m.Blocks.Select((block, index) => (block, index)).Where(x => x.block is ParagraphBlock p && p.Runs.Any(r => r.Bookmark == name)).ToArray();
            if (matches.Length != 1 || matches[0].block is not ParagraphBlock placeholder || placeholder.Control != null || placeholder.NestedControl != null)
                throw new InvalidDataException("Fragment.Placeholder: A unique unwrapped body bookmark paragraph is required.");
            int start = placeholder.Runs.FindIndex(r => r.Bookmark == name), end = placeholder.Runs.FindIndex(r => r.Bookmark == "/" + name);
            if (start != 0 || end != placeholder.Runs.Count - 1 || end <= start
                || placeholder.Runs.Skip(1).Take(end - 1).Any(r => r.Bookmark != null || r.Control != null || r.Note != null || r.Image != null || r.FieldInstruction != null || r.Link != null || r.Box != null || r.Object != null))
                throw new NotSupportedException("Fragment.Placeholder: The bookmark must own the complete plain-text placeholder paragraph.");
            if (m.Paragraphs.SelectMany(p => p.Runs).Any(r => r.FieldInstruction != null || r.FieldChildren != null || r.FieldCache != null
                || r.Link != null && Starts(r.Link, '#') && string.Equals(r.Link.Substring(1), name, StringComparison.OrdinalIgnoreCase)))
                throw new NotSupportedException("Fragment.Reference: Target fields or links to the placeholder require explicit resolution before insertion.");
            string prefix = UniquePrefix(m); int nextId = 1; RemapTemplateControlIds(incoming, TemplateControlIds(m), ref nextId, c);
            incoming.CustomProperties.Clear(); MergeMetadata(m, incoming, prefix, c);
            Remap(incoming, prefix, m.Notes.Select(n => n.PartId).DefaultIfEmpty().Max());
            foreach (DocumentBlock block in incoming.Blocks) block.Section = placeholder.Section;
            m.Blocks.RemoveAt(matches[0].index); m.Blocks.InsertRange(matches[0].index, incoming.Blocks); m.Notes.AddRange(incoming.Notes);
            ValidateRanges(m); return incoming.Blocks.Count;
        }, cancellationToken);
    }
}
