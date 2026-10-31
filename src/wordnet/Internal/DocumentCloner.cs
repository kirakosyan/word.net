using System.Collections.Generic;
using System.Linq;
using WordNet.Model;

namespace WordNet.Internal;

internal sealed class DocumentCloner
{
    private readonly Dictionary<PageSettings, PageSettings> sections = new Dictionary<PageSettings, PageSettings>();
    private readonly Dictionary<DocumentBlock, DocumentBlock> blocks = new Dictionary<DocumentBlock, DocumentBlock>();
    private readonly Dictionary<DocumentNote, DocumentNote> notes = new Dictionary<DocumentNote, DocumentNote>();
    private readonly Dictionary<TemplateControl, TemplateControl> controls = new Dictionary<TemplateControl, TemplateControl>();
    private readonly Dictionary<ListLevel[], ListLevel[]> lists = new Dictionary<ListLevel[], ListLevel[]>();
    private readonly Dictionary<List<DocumentBlock>, List<DocumentBlock>> stories = new Dictionary<List<DocumentBlock>, List<DocumentBlock>>();
    private readonly ConversionContext context;
    internal DocumentCloner(ConversionContext context) { this.context = context; }
    internal DocumentModel Copy(DocumentModel source)
    {
        var target = new DocumentModel { Title = source.Title, Author = source.Author, ObjectCount = source.ObjectCount, Controls = source.Controls.Copy(context), HasControlDisplay = source.HasControlDisplay, HasLinkedTextBoxes = source.HasLinkedTextBoxes, Theme = source.Theme };
        foreach (var property in source.CustomProperties) target.CustomProperties.Add(property.Key, property.Value);
        foreach (var style in source.Styles) target.Styles.Add(style.Key, style.Value);
        target.Blocks.AddRange(source.Blocks.Select(Block)); target.FinalSection = Section(source.FinalSection);
        target.Notes.AddRange(source.Notes.Select(Note));
        foreach (var pair in source.Review.Revisions) target.Review.Revisions.Add(pair.Key, Review(pair.Value));
        foreach (var pair in source.Review.Comments) target.Review.Comments.Add(pair.Key, Review(pair.Value));
        return target;
    }
    internal DocumentBlock Block(DocumentBlock source)
    {
        context.CheckCancellation();
        if (blocks.TryGetValue(source, out DocumentBlock? existing)) return existing;
        DocumentBlock result;
        if (source is ParagraphBlock p)
        {
            var paragraph = new ParagraphBlock { FallbackFontSize = p.FallbackFontSize, FallbackAfter = p.FallbackAfter, StyleId = p.StyleId, Mark = p.Mark == null ? null : Run(p.Mark), StructuralRevision = p.StructuralRevision, ParagraphFormatRevision = p.ParagraphFormatRevision, PreviousParagraphFormat = p.PreviousParagraphFormat?.Copy(), ExplicitParagraphFormat = p.ExplicitParagraphFormat, RemoveWhenObjectEmpty = p.RemoveWhenObjectEmpty, Identity = p.Identity, RightToLeft = p.RightToLeft, HeadingLevel = p.HeadingLevel, Alignment = p.Alignment, IsCodeBlock = p.IsCodeBlock, Layout = p.Layout.Copy() };
            if (p.List != null)
            {
                paragraph.List = p.List.Copy();
                if (p.List.Definition != null)
                {
                    if (!lists.TryGetValue(p.List.Definition, out ListLevel[]? definition))
                    {
                        definition = p.List.Definition.Select(l =>
                        {
                            var level = new ListLevel
                            {
                                Format = l.Format,
                                Pattern = l.Pattern,
                                Start = l.Start,
                                RestartAfter = l.RestartAfter,
                                Legal = l.Legal,
                                Suffix = l.Suffix,
                                Layout = l.Layout.Copy(),
                                Appearance = Run(l.Appearance)
                            };
                            level.LayoutProperties.UnionWith(l.LayoutProperties); return level;
                        }).ToArray();
                        lists.Add(p.List.Definition, definition);
                    }
                    paragraph.List.Definition = definition;
                }
            }
            paragraph.Runs.AddRange(p.Runs.Select(Run)); result = paragraph;
        }
        else
        {
            var table = (TableBlock)source;
            var copy = new TableBlock
            {
                HeaderRowCount = table.HeaderRowCount,
                Width = table.Width,
                WidthPercent = table.WidthPercent,
                AutoFit = table.AutoFit,
                ManagedImplicitAutoFit = table.ManagedImplicitAutoFit,
                LayoutExplicit = table.LayoutExplicit,
                LeftIndent = table.LeftIndent,
                Background = table.Background,
                Style = table.Style
            };
            copy.ColumnWidths.AddRange(table.ColumnWidths); copy.NonSplittingRows.UnionWith(table.NonSplittingRows);
            foreach (var border in table.Borders) copy.Borders.Add(border.Key, Border(border.Value));
            foreach (var item in table.RowRevisions) copy.RowRevisions.Add(item.Key, item.Value);
            foreach (var item in table.RowLefts) copy.RowLefts.Add(item.Key, item.Value);
            foreach (var item in table.RowHeights) copy.RowHeights.Add(item.Key, item.Value);
            foreach (var item in table.RowControls) copy.RowControls.Add(item.Key, Control(item.Value)!);
            foreach (List<TableCell> row in table.Rows) copy.Rows.Add(row.Select(Cell).ToList());
            result = copy;
        }
        blocks.Add(source, result);
        result.TypedControlId = source.TypedControlId; result.Section = Section(source.Section); result.Location = source.Location; result.Control = Control(source.Control); result.NestedControl = Control(source.NestedControl); return result;
    }
    internal TableCell Cell(TableCell source)
    {
        var cell = new TableCell
        {
            CellWidth = source.CellWidth,
            WidthPercent = source.WidthPercent,
            Padding = source.Padding?.Copy(),
            ColumnSpan = source.ColumnSpan,
            Merge = source.Merge,
            VerticalAlignment = source.VerticalAlignment,
            Background = source.Background
        };
        foreach (var border in source.Borders) cell.Borders.Add(border.Key, Border(border.Value));
        cell.Content.AddRange(source.Blocks.Select(Block));
        cell.Paragraphs.AddRange(DocumentModel.CellParagraphs(cell.Content));
        return cell;
    }
    private TextRun Run(TextRun r)
    {
        PositionedBox? box = null;
        if (r.Box != null)
        {
            box = new PositionedBox { Anchor = Anchor(r.Box.Anchor), Width = r.Box.Width, Height = r.Box.Height, Rotation = r.Box.Rotation, ShrinkToFit = r.Box.ShrinkToFit, StoryId = r.Box.StoryId, ImplicitStoryId = r.Box.ImplicitStoryId, StorySequence = r.Box.StorySequence, Geometry = r.Box.Geometry, AlternativeText = r.Box.AlternativeText, Fill = r.Box.Fill, Border = r.Box.Border == null ? null : Border(r.Box.Border), Padding = r.Box.Padding.Copy() };
            box.Blocks.AddRange(r.Box.Blocks.Select(Block));
        }
        DocumentImage? image = r.Image?.Scaled(1);
        if (image?.Anchor != null) image.Anchor = Anchor(image.Anchor);
        return new TextRun(r.Text, r.Bold, r.Italic, r.Code, r.Link, r.FontSize, r.Color, image, r.Appearance.Copy(), r.FieldInstruction,
            r.Note == null ? null : Note(r.Note), box, r.Bookmark, Control(r.Control), r.Object)
        { ControlDisplay = r.ControlDisplay, ControlGlyph = r.ControlGlyph, TypedControlId = r.TypedControlId, BidiLevel = r.BidiLevel, FieldChildren = r.FieldChildren?.ToDictionary(p => p.Key, p => Run(p.Value)), FieldCache = r.FieldCache?.Select(Run).ToList(), Revision = r.Revision, FormatRevision = r.FormatRevision, PreviousFormat = r.PreviousFormat == null ? null : Run(r.PreviousFormat), ExplicitFormat = r.ExplicitFormat, Comment = r.Comment, CommentBoundary = r.CommentBoundary };
    }
    private ReviewRecord Review(ReviewRecord source)
    {
        var result = new ReviewRecord { Id = source.Id, Author = source.Author, Date = source.Date, Deletion = source.Deletion, Structure = source.Structure, MoveSide = source.MoveSide, MovePartner = source.MovePartner, Initials = source.Initials };
        result.Body.AddRange(source.Body.Select(p => (ParagraphBlock)Block(p))); return result;
    }
    private PageSettings? Section(PageSettings? source)
    {
        if (source == null) return null;
        if (sections.TryGetValue(source, out PageSettings? existing)) return existing;
        PageSettings copy = source.EditingCopy(); sections.Add(source, copy);
        copy.Footnotes = Policy(source.Footnotes); copy.Endnotes = Policy(source.Endnotes);
        foreach (var story in source.Stories)
        {
            if (!stories.TryGetValue(story.Value, out List<DocumentBlock>? content))
            {
                content = new List<DocumentBlock>(); stories.Add(story.Value, content);
                content.AddRange(story.Value.Select(Block));
            }
            copy.Stories.Add(story.Key, content);
        }
        return copy;
    }
    private DocumentNote Note(DocumentNote source)
    {
        if (notes.TryGetValue(source, out DocumentNote? existing)) return existing;
        var note = new DocumentNote { ExplicitReferenceMark = source.ExplicitReferenceMark, Id = source.Id, Endnote = source.Endnote, PartId = source.PartId, Number = source.Number, Format = source.Format, Policy = Policy(source.Policy) };
        notes.Add(source, note); note.Blocks.AddRange(source.Blocks.Select(Block)); return note;
    }
    private TemplateControl? Control(TemplateControl? source)
    {
        if (source == null) return null;
        if (!controls.TryGetValue(source, out TemplateControl? copy)) { copy = new TemplateControl(source.Tag) { Executable = source.Executable, Id = source.Id, Alias = source.Alias }; controls.Add(source, copy); }
        return copy;
    }
    private static NotePolicy Policy(NotePolicy p) => new NotePolicy { Format = p.Format, Start = p.Start, Restart = p.Restart };
    private static CellBorder Border(CellBorder b) => new CellBorder { Color = b.Color, Style = b.Style, Width = b.Width };
    private static DrawingAnchor Anchor(DrawingAnchor a) => new DrawingAnchor
    {
        HorizontalOrigin = a.HorizontalOrigin,
        VerticalOrigin = a.VerticalOrigin,
        HorizontalAlignment = a.HorizontalAlignment,
        VerticalAlignment = a.VerticalAlignment,
        X = a.X,
        Y = a.Y,
        Wrap = a.Wrap,
        Behind = a.Behind,
        UnqualifiedLayer = a.UnqualifiedLayer,
        RelativeHeight = a.RelativeHeight,
        Distance = a.Distance.Copy()
    };
}
