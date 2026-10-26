using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using WordNet.Internal;
using WordNet.Model;

namespace WordNet.Formats;

internal sealed partial class DocxReader
{
    private static readonly XNamespace W = DocxPackage.Word;
    private readonly ConversionContext context;
    private readonly Dictionary<string, XElement> relationships;
    private readonly Dictionary<string, XElement> styles = new Dictionary<string, XElement>(StringComparer.Ordinal);
    private string? defaultStyle;
    private ParagraphAlignment defaultAlignment;
    private ParagraphLayout defaultLayout = new ParagraphLayout();
    private TextStyle defaultRun = new TextStyle();
    private string title = "";
    private string author = "";
    private bool evenAndOdd;
    private readonly Dictionary<int, string> bookmarks = new Dictionary<int, string>();

    private DocxReader(ConversionContext context, Dictionary<string, XElement> relationships)
    {
        this.context = context;
        this.relationships = relationships;
    }

    internal static DocumentModel Read(Stream input, ConversionContext context)
    {
        using var package = new DocxPackage(input, context);
        var rootRelations = package.ReadRelationships("").Values;
        var mainRelations = rootRelations
            .Where(relation => (string?)relation.Attribute("Type") == DocxPackage.DocumentRelationships.NamespaceName + "/officeDocument").ToArray();
        if (mainRelations.Length != 1)
        {
            throw new InvalidDataException("Docx.MainPart: Exactly one document relationship is required.");
        }

        string main = DocxPackage.ResolvePart("", mainRelations[0]);
        XDocument types = package.ReadXml("[Content_Types].xml");
        if (types.Root?.Name != DocxPackage.ContentTypes + "Types"
            || !types.Root.Elements(DocxPackage.ContentTypes + "Override").Any(item =>
                (string?)item.Attribute("PartName") == "/" + main && (string?)item.Attribute("ContentType") == DocxPackage.MainContentType))
        {
            throw new InvalidDataException("Docx.ContentType: A non-macro DOCX main document content type is required.");
        }

        var reader = new DocxReader(context, package.ReadRelationships(main)) { mainPart = main };
        var knownParts = new HashSet<string>(StringComparer.Ordinal)
        {
            "[Content_Types].xml", "_rels/.rels", main, DocxPackage.RelationshipPartName(main),
        };
        XElement[] styleRelations = reader.relationships.Values.Where(relation =>
            (string?)relation.Attribute("Type") == DocxPackage.DocumentRelationships.NamespaceName + "/styles").ToArray();
        if (styleRelations.Length > 1)
        {
            throw new InvalidDataException("Docx.Styles: Multiple style parts are not valid.");
        }

        if (styleRelations.Length == 1)
        {
            string stylePart = DocxPackage.ResolvePart(main, styleRelations[0]);
            using (context.At(new SourceLocation(stylePart, "/"))) reader.ReadStyles(package.ReadXml(stylePart));
            knownParts.Add(stylePart);
        }

        reader.package = package;
        reader.mainPart = main;
        reader.knownParts = knownParts;
        reader.ReadNumbering();
        reader.ReadNotes();
        reader.ReadReviewComments();
        reader.ReadAuxiliaryParts("", rootRelations);
        reader.ReadAuxiliaryParts(main, reader.relationships.Values);

        XDocument source = package.ReadXml(main);
        if (source.Root?.Name != W + "document")
        {
            throw new NotSupportedException("Docx.Namespace: Only Transitional WordprocessingML is supported in this development version.");
        }

        XElement[] bodies = source.Root.Elements(W + "body").ToArray();
        if (bodies.Length != 1)
        {
            throw new InvalidDataException("Docx.Body: Exactly one document body is required.");
        }

        reader.PrepareTypedControls(source);
        var document = new DocumentModel();
        reader.ValidateReviewSource(bodies[0]);
        reader.ReadBlocks(bodies[0], document.Blocks);
        if (reader.sectionCount > 0 && bodies[0].Element(W + "sectPr") == null)
            reader.EndSection(new XElement(W + "sectPr"), document.Blocks);
        reader.PruneTocParagraphs(document.Blocks);
        document.FinalSection = reader.finalSection;
        document.Notes.AddRange(reader.notes.Values.Where(n => n.Number > 0));
        var noteCounters = new Dictionary<(bool Endnote, PageSettings? Section), int>();
        var numberedNotes = new HashSet<DocumentNote>();
        foreach (DocumentBlock block in document.Blocks)
        {
            PageSettings? section = block.Section;
            IEnumerable<ParagraphBlock> paragraphs = DocumentModel.Expand(new[] { block }).OfType<ParagraphBlock>();
            foreach (DocumentNote note in paragraphs.SelectMany(p => p.Runs).Where(r => r.Note != null).Select(r => r.Note!))
            {
                if (!numberedNotes.Add(note)) continue;
                NotePolicy policy = (note.Endnote ? section?.Endnotes : section?.Footnotes) ?? new NotePolicy();
                var key = (note.Endnote, policy.Restart == "eachSect" ? section : null);
                int number = noteCounters.TryGetValue(key, out int previous) ? checked(previous + 1) : policy.Start;
                noteCounters[key] = number; note.Number = number; note.Format = policy.Format; note.Policy = policy;
            }
        }
        document.Title = reader.title; document.Author = reader.author;
        foreach (var property in reader.customProperties) document.CustomProperties.Add(property.Key, property.Value);
        CustomPropertyValues.Validate(document, context);
        foreach (var pair in reader.styles.Where(p => (string?)p.Value.Attribute(W + "type") == "paragraph"))
            document.Styles.Add(pair.Key, (string?)pair.Value.Element(W + "name")?.Attribute(W + "val") ?? pair.Key);
        foreach (var pair in reader.review.Revisions) document.Review.Revisions.Add(pair.Key, pair.Value);
        foreach (var pair in reader.review.Comments) document.Review.Comments.Add(pair.Key, pair.Value);
        if (context.PreserveReview) document.Review.Validate(document, context);
        if (reader.fields.Count != 0) throw new InvalidDataException("Docx.Field: Unclosed complex field.");
        if (package.PartNames.Any(name => !knownParts.Contains(name)))
            context.Loss("Docx.AdditionalParts", "Unrecognized parts such as headers, annotations or embedded content are not preserved in this conversion profile.");
        document.ObjectCount = context.ObjectCount;
        reader.FinishTypedControls(document);
        if (document.ObjectCount != 0) { ObjectSafety.Owners(document); context.Objects.Finish(document.CharacterCount, 0); }
        return document;
    }

    private void ReadAuxiliaryParts(string owner, IEnumerable<XElement> relations)
    {
        foreach (XElement relation in relations)
        {
            string type = (string?)relation.Attribute("Type") ?? "";
            bool metadata = type == "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties"
                || type == DocxPackage.DocumentRelationships.NamespaceName + "/extended-properties"
                || type == DocxPackage.DocumentRelationships.NamespaceName + "/custom-properties";
            bool catalog = type == DocxPackage.DocumentRelationships.NamespaceName + "/fontTable"
                || type == DocxPackage.DocumentRelationships.NamespaceName + "/theme"
                || type == DocxPackage.DocumentRelationships.NamespaceName + "/webSettings";
            bool settings = type == DocxPackage.DocumentRelationships.NamespaceName + "/settings";
            if (!metadata && !catalog && !settings) continue;
            string path = DocxPackage.ResolvePart(owner, relation);
            XElement root = package.ReadXml(path).Root ?? throw new InvalidDataException("Docx.AuxiliaryPart: Missing root.");
            using var location = context.At(XmlSourceLocation.Create(root, path));
            knownParts.Add(path);
            if (type == DocxPackage.DocumentRelationships.NamespaceName + "/custom-properties") ReadCustomProperties(root);
            if (metadata)
            {
                XNamespace dc = "http://purl.org/dc/elements/1.1/";
                title = root.Element(dc + "title")?.Value ?? title;
                author = root.Element(dc + "creator")?.Value ?? author;
            }
            if (settings && root.Element(W + "evenAndOddHeaders") is XElement parity) evenAndOdd = IsOn(parity);
            if (settings && root.Elements().Any(e => e.Name != W + "proofState" && e.Name != W + "zoom"
                && e.Name != W + "rsids" && e.Name != W + "defaultTabStop" && e.Name != W + "characterSpacingControl" && e.Name != W + "evenAndOddHeaders"))
                context.Loss("Docx.Settings", "Unsupported document settings may affect layout or editing behavior and are not retained.");
            if (settings && root.Elements().Any(e => e.Name == W + "defaultTabStop" || e.Name == W + "characterSpacingControl"))
                context.Loss("Docx.Settings", "Tab and character-spacing settings are not retained.");
            context.Report("Docx.AuxiliaryMetadata", "Document properties, font/theme catalogs and editor metadata are not retained; unsupported content formatting is diagnosed separately.");
        }
    }

    private void ReadStyles(XDocument source)
    {
        XElement root = source.Root ?? throw new InvalidDataException("Docx.Styles: Missing style root.");
        if (root.Name != W + "styles")
        {
            throw new InvalidDataException("Docx.Styles: Invalid style namespace.");
        }

        var defaults = new TextStyle { Layout = defaultLayout.Copy() };
        ReadParagraphProperties(root.Element(W + "docDefaults")?.Element(W + "pPrDefault")?.Element(W + "pPr"), defaults);
        defaultAlignment = defaults.Alignment;
        defaultLayout = defaults.Layout.Copy();
        ReadRunProperties(root.Element(W + "docDefaults")?.Element(W + "rPrDefault")?.Element(W + "rPr"), defaultRun);

        foreach (XElement style in root.Elements(W + "style"))
        {
            string id = DocxPackage.RequiredAttribute(style, W + "styleId");
            if (styles.ContainsKey(id))
            {
                throw new InvalidDataException("Docx.Styles: Duplicate style identifier.");
            }

            styles.Add(id, style);
            if (styles.Count > context.Options.MaximumCharacters) throw new InvalidDataException("Limit.Styles: Too many styles.");
            if ((string?)style.Attribute(W + "type") == "paragraph" && IsTrue((string?)style.Attribute(W + "default")))
            {
                if (defaultStyle != null) context.Report("Docx.MultipleDefaultStyles", "The last paragraph style marked default is used, following the document format default-style rule.");
                defaultStyle = id;
            }
        }
    }

    private void ReadBlocks(XElement container, List<DocumentBlock> blocks)
    {
        var pendingBookmarks = new List<TextRun>();
        foreach (XElement element in container.Elements())
        {
            context.CheckCancellation();
            using var location = element.Name == W + "p" || element.Name == W + "tbl" ? null : context.At(XmlSourceLocation.Create(element, mainPart));
            if (BlockBookmark(element, blocks, pendingBookmarks)) continue;
            int controlStart = blocks.Count;
            if (ReadBlockControl(element, blocks))
            {
                if (blocks.Count > controlStart) AttachBookmarks(pendingBookmarks, new[] { blocks[controlStart] }, true);
                continue;
            }
            if (Container(element, out XElement? content))
            {
                int first = blocks.Count;
                if (content != null) ReadBlocks(content, blocks);
                if (blocks.Count > first) AttachBookmarks(pendingBookmarks, new[] { blocks[first] }, true);
                continue;
            }
            if (element.Name == W + "p")
            {
                blocks.Add(ReadParagraph(element));
                AttachBookmarks(pendingBookmarks, new[] { blocks[blocks.Count - 1] }, true);
                if (element.Element(W + "pPr")?.Element(W + "sectPr") is XElement section) EndSection(section, blocks, paragraphBoundary: true);
            }
            else if (element.Name == W + "tbl")
            {
                blocks.Add(ReadTable(element));
                AttachBookmarks(pendingBookmarks, new[] { blocks[blocks.Count - 1] }, true);
            }
            else if (element.Name == W + "sectPr")
            {
                EndSection(element, blocks);
            }
            else
            {
                context.Loss("Docx.Block", "Unsupported document block structure is flattened where supported content is available.");
                if (element.Name != W + "del" && element.Name != W + "moveFrom")
                {
                    ReadBlocks(element, blocks);
                }
            }
        }
        if (pendingBookmarks.Count > 0)
        {
            if (!DocumentModel.Expand(blocks).OfType<ParagraphBlock>().Any()) blocks.Add(new ParagraphBlock());
            AttachBookmarks(pendingBookmarks, blocks, false);
        }
    }

    private ParagraphBlock ReadParagraph(XElement element)
    {
        using var location = context.At(XmlSourceLocation.Create(element, mainPart));
        var style = ResolveStyle(defaultStyle, new HashSet<string>(StringComparer.Ordinal));
        XElement? properties = element.Element(W + "pPr");
        string? styleId = (string?)properties?.Element(W + "pStyle")?.Attribute(W + "val");
        if (styleId != null)
        {
            style = ResolveStyle(styleId, new HashSet<string>(StringComparer.Ordinal));
        }

        ReadParagraphProperties(properties, style);
        ListItem? list = ResolveList(style);
        if (list?.Definition?[list.Level] is ListLevel listLevel && listLevel.LayoutProperties.Count > 0)
        {
            listLevel.ApplyLayout(style.Layout);
            ReadParagraphProperties(properties, style);
        }
        var mark = style.Copy();
        XElement? markProperties = properties?.Element(W + "rPr");
        if (markProperties != null && context.PreserveReview)
            markProperties = new XElement(W + "rPr", markProperties.Elements().Where(e => e.Name != W + "ins" && e.Name != W + "del"));
        ReadRunProperties(markProperties, mark);
        string? retainedStyle = styleId ?? defaultStyle;
        if (retainedStyle != null && (!styles.TryGetValue(retainedStyle, out XElement? retainedDefinition) || (string?)retainedDefinition.Attribute(W + "type") != "paragraph")) retainedStyle = null;
        var paragraph = new ParagraphBlock { FallbackFontSize = style.Heading == 0 ? 11 : null, FallbackAfter = 0, StyleId = retainedStyle, Mark = new TextRun("", mark.Bold, mark.Italic, mark.Code, fontSize: mark.FontSize, color: mark.Color, appearance: mark.Appearance.Copy()), RightToLeft = style.RightToLeft, Location = context.Location, HeadingLevel = style.Heading, IsCodeBlock = style.CodeBlock, Alignment = style.Alignment, List = list, Layout = style.Layout.Copy() };
        paragraph.StructuralRevision = ReadStructuralRevision(properties?.Element(W + "rPr"), "paragraph");
        ReadInlines(element, paragraph.Runs, style, null);
        return paragraph;
    }

    private void ReadInlines(XElement element, List<TextRun> runs, TextStyle inherited, string? link)
    {
        foreach (XElement child in element.Elements())
        {
            context.CheckCancellation();
            using var location = context.At(XmlSourceLocation.Create(child, mainPart));
            if (ReviewInline(child, runs, inherited, link)) continue;
            if (ReadInlineControl(child, runs, inherited, link)) continue;
            if (Container(child, out XElement? content))
            {
                if (content != null) ReadInlines(content, runs, inherited, link);
                continue;
            }
            if (child.Name == W + "pPr" || child.Name == W + "rPr")
            {
                continue;
            }

            if (child.Name == W + "bookmarkStart" || child.Name == W + "bookmarkEnd")
            {
                TextRun? marker = ReadBookmark(child);
                if (marker != null) Emit(runs, marker);
                continue;
            }
            if (child.Name == W + "fldChar") { FieldBoundary((string?)child.Attribute(W + "fldCharType"), runs); continue; }
            if (child.Name == W + "instrText")
            {
                if (fields.Count == 0) context.Report("Field.OrphanInstruction", "An orphan field instruction is omitted.");
                else if (!fields.Peek().Separated) fields.Peek().Instruction.Append(child.Value);
                continue;
            }
            if (child.Name == W + "fldSimple")
            {
                if (fields.Count >= context.Options.MaximumNestingDepth) throw new InvalidDataException("Limit.NestingDepth: Fields exceed the nesting limit.");
                fields.Push(new FieldCapture { Separated = true });
                ReadInlines(child, runs, inherited, link);
                FieldCapture field = fields.Pop();
                if (fields.Count > 0 && !fields.Peek().Separated) CaptureChild(DocxPackage.RequiredAttribute(child, W + "instr"), field);
                else EmitField(runs, DocxPackage.RequiredAttribute(child, W + "instr"), field.Result, field.NestedInstruction, field.RetainTree ? field.Children : null);
                continue;
            }

            if (child.Name == W + "r")
            {
                TextStyle style = inherited.Copy();
                ReadRunProperties(child.Element(W + "rPr"), style);
                ReadInlines(child, runs, style, link);
            }
            else if (child.Name == W + "t")
            {
                Emit(runs, new TextRun(child.Value, inherited.Bold, inherited.Italic, inherited.Code, link, inherited.FontSize, inherited.Color, appearance: inherited.Appearance.Copy()));
            }
            else if (child.Name == W + "tab")
            {
                Emit(runs, new TextRun("\t", inherited.Bold, inherited.Italic, inherited.Code, link, inherited.FontSize, inherited.Color, appearance: inherited.Appearance.Copy()));
            }
            else if (child.Name == W + "br" || child.Name == W + "cr")
            {
                string? type = (string?)child.Attribute(W + "type");
                if (type is not null and not "textWrapping" and not "page" and not "column") throw new InvalidDataException("Docx.Break: Invalid break type.");
                Emit(runs, new TextRun(type == "page" ? "\f" : type == "column" ? "\v" : "\n", inherited.Bold, inherited.Italic, inherited.Code, link, inherited.FontSize, inherited.Color, appearance: inherited.Appearance.Copy()));
            }
            else if (child.Name == MathObjectFormat.M + "oMath" || child.Name == MathObjectFormat.M + "oMathPara")
            {
                DocumentObjectData? retained = ReadObjectForConversion(() => ReadRetainedMath(child));
                if (retained != null) Emit(runs, new TextRun("", retainedObject: retained));
            }
            else if (child.Name == W + "drawing")
            {
                DocumentObjectData? retained = ReadObjectForConversion(() => ReadRetainedDrawing(child));
                if (retained != null) { Emit(runs, new TextRun("", retainedObject: retained)); continue; }
                if (child.Descendants(ChartObjectFormat.C + "chart").Any()) continue;
                XElement? box = child.Descendants(W + "txbxContent").FirstOrDefault();
                if (box != null)
                {
                    Emit(runs, new TextRun("", box: ReadBox(child, box)));
                    continue;
                }
                DocumentImage? image = ReadDrawing(child);
                if (image != null) Emit(runs, new TextRun("", link: link, image: image));
            }
            else if (child.Name == W + "footnoteReference" || child.Name == W + "endnoteReference")
            {
                string id = Integer(child, W + "id", 0, int.MaxValue).ToString(CultureInfo.InvariantCulture);
                if (!noteIds.Contains((child.Name == W + "footnoteReference" ? "footnotes:" : "endnotes:") + id))
                    throw new InvalidDataException("Docx.Notes: Unresolved note reference.");
                DocumentNote note = notes[(child.Name == W + "footnoteReference" ? "footnotes:" : "endnotes:") + id];
                if (note.Number == 0) note.Number = notes.Values.Count(n => n.Endnote == note.Endnote && n.Number > 0) + 1;
                Emit(runs, new TextRun("", inherited.Bold, inherited.Italic, inherited.Code, link, inherited.FontSize, inherited.Color,
                    appearance: inherited.Appearance.Copy(), note: note));
            }
            else if (child.Name == W + "footnoteRef" || child.Name == W + "endnoteRef")
            {
                if (noteReferenceMark || runs.Any(r => r.Text.Length != 0 || r.Image != null || r.FieldInstruction != null))
                    context.Loss("Notes.ReferenceMark", "Only one leading note reference mark is retained; unsupported placement uses a leading label.");
                noteReferenceMark = true;
            }
            else if (child.Name == W + "hyperlink")
            {
                string? id = (string?)child.Attribute(DocxPackage.DocumentRelationships + "id");
                string? target = null;
                if (id != null && relationships.TryGetValue(id, out XElement? relation)
                    && (string?)relation.Attribute("Type") == DocxPackage.DocumentRelationships.NamespaceName + "/hyperlink"
                    && (string?)relation.Attribute("TargetMode") == "External")
                {
                    target = context.SafeLink((string?)relation.Attribute("Target"));
                }
                else if ((string?)child.Attribute(W + "anchor") is string bookmark) target = "#" + bookmark;
                else
                {
                    context.Loss("Docx.Hyperlink", "An internal or unresolved hyperlink becomes plain text.");
                }

                ReadInlines(child, runs, inherited, target);
            }
            else
            {
                context.Loss("Docx.Inline", "Unsupported inline content or annotations are flattened where text is available.");
                if (child.Name != W + "del" && child.Name != W + "moveFrom" && child.Name != W + "delText" && child.Name != W + "instrText")
                {
                    ReadInlines(child, runs, inherited, link);
                }
            }
        }
    }

    private TableBlock ReadTable(XElement element)
    {
        using var location = context.At(XmlSourceLocation.Create(element, mainPart));
        var table = new TableBlock { Location = context.Location };
        var pendingTableBookmarks = new List<TextRun>();
        ReadTableGeometry(element, table);
        foreach (XElement child in TableContents(element))
        {
            context.CheckCancellation();
            if (BlockBookmark(child, new[] { table }, pendingTableBookmarks)) continue;
            if (child.Name == W + "tblPr" || child.Name == W + "tblGrid")
            {
                continue;
            }

            if (child.Name != W + "tr")
            {
                context.Loss("Docx.TableStructure", "Unsupported table structure is omitted.");
                continue;
            }

            using var rowLocation = context.At(XmlSourceLocation.Create(child, mainPart));
            if (rowControls.TryGetValue(child, out TemplateControl? control)) table.RowControls.Add(table.Rows.Count, control);
            var cells = new List<TableCell>();
            foreach (XElement rowChild in TableContents(child))
            {
                if (BlockBookmark(rowChild, table.Rows.SelectMany(r => r).Concat(cells).SelectMany(c => c.Blocks), pendingTableBookmarks)) continue;
                if (rowChild.Name == W + "trPr")
                {
                    XElement? header = rowChild.Element(W + "tblHeader");
                    if (header != null && IsOn(header))
                    {
                        if (table.HeaderRowCount == table.Rows.Count) table.HeaderRowCount++;
                        else context.Loss("Docx.TableHeaders", "Noncontiguous repeating header rows are not retained.");
                    }
                    if (rowChild.Element(W + "cantSplit") is XElement cantSplit && IsOn(cantSplit)) table.NonSplittingRows.Add(table.Rows.Count);
                    if (rowChild.Element(W + "trHeight") is XElement height)
                    {
                        string rule = (string?)height.Attribute(W + "hRule") ?? "atLeast";
                        if (rule is not "atLeast" and not "exact" and not "auto") throw new InvalidDataException("Docx.RowHeight: Invalid height rule.");
                        if (rule != "auto") table.RowHeights.Add(table.Rows.Count, (Integer(height, W + "val", 0, 31680) / 20.0, rule == "exact"));
                    }
                    if (rowChild.Elements().Any(property => property.Name != W + "trHeight" && property.Name != W + "tblHeader" && property.Name != W + "cantSplit" && property.Name != W + "gridBefore" && property.Name != W + "gridAfter" && !(context.PreserveReview && (property.Name == W + "ins" || property.Name == W + "del"))))
                    {
                        context.Loss("Docx.TableFormatting", "Table row formatting is not retained.");
                    }

                    continue;
                }

                if (rowChild.Name != W + "tc")
                {
                    context.Loss("Docx.TableStructure", "Unsupported table-row content is omitted.");
                    continue;
                }

                using var cellLocation = context.At(XmlSourceLocation.Create(rowChild, mainPart));
                var cell = new TableCell();
                var pendingBookmarks = new List<TextRun>();
                ReadCellGeometry(element.Element(W + "tblPr")?.Element(W + "tblCellMar"), rowChild.Element(W + "tcPr"), cell);
                foreach (XElement content in TableContents(rowChild))
                {
                    if (content.Name == W + "tcPr") continue;
                    if (BlockBookmark(content, cell.Content, pendingBookmarks)) continue;
                    int previousBlocks = cell.Content.Count;
                    if (ReadBlockControl(content, cell.Content))
                    {
                        cell.Paragraphs.AddRange(DocumentModel.CellParagraphs(cell.Content.Skip(previousBlocks)));
                        AttachBookmarks(pendingBookmarks, cell.Content.Skip(previousBlocks), true);
                        continue;
                    }
                    if (content.Name == W + "tbl")
                    {
                        TableBlock nested = ReadTable(content); cell.Content.Add(nested);
                        AttachBookmarks(pendingBookmarks, new[] { nested }, true);
                        cell.Paragraphs.AddRange(nested.Rows.SelectMany(r => r).SelectMany(c => c.Paragraphs));
                        continue;
                    }
                    if (content.Name != W + "p")
                    {
                        context.Loss("Docx.TableCell", "Nested tables and unsupported cell content are not retained.");
                        continue;
                    }

                    ParagraphBlock paragraph = ReadParagraph(content); cell.Paragraphs.Add(paragraph); cell.Content.Add(paragraph);
                    AttachBookmarks(pendingBookmarks, new[] { paragraph }, true);
                }
                if (cell.Paragraphs.Count == 0) { var empty = new ParagraphBlock(); cell.Paragraphs.Add(empty); cell.Content.Add(empty); }
                AttachBookmarks(pendingBookmarks, cell.Content, false);
                AttachBookmarks(pendingTableBookmarks, cell.Blocks, true);
                cells.Add(cell);
            }

            XElement? rowProperties = child.Element(W + "trPr");
            string? rowRevision = ReadStructuralRevision(rowProperties, "row");
            if (rowRevision != null) table.RowRevisions.Add(table.Rows.Count, rowRevision);
            Omitted("gridBefore", true); Omitted("gridAfter", false);
            table.Rows.Add(cells);
            void Omitted(string name, bool before)
            {
                if (rowProperties?.Element(W + name) is not XElement omitted) return;
                int count = Integer(omitted, W + "val", 0, 1024);
                if (count == 0) return;
                context.Loss("Docx.TableGrid", "Omitted grid columns are represented by empty cells.");
                var gap = new TableCell(new ParagraphBlock()) { ColumnSpan = count, Padding = new CellPadding() };
                if (before) cells.Insert(0, gap); else cells.Add(gap);
            }
        }

        AttachBookmarks(pendingTableBookmarks, new[] { table }, false);
        return table;
    }

    private IEnumerable<XElement> TableContents(XElement container)
    {
        foreach (XElement child in container.Elements())
        {
            if (child.Name == W + "sdt" || child.Name == W + "sdtContent" || child.Name == W + "customXml")
            {
                if (context.Editing && container.Name == W + "tc" && child.Name == W + "sdt") { yield return child; continue; }
                XElement? content = child.Name == W + "sdt" ? child.Element(W + "sdtContent") : child;
                TemplateControl? control = context.Editing && child.Name == W + "sdt" ? ReadControl(child) : null;
                XElement[] nestedContent = content == null ? Array.Empty<XElement>() : TableContents(content).ToArray();
                bool rows = control != null && nestedContent.Length > 0 && nestedContent.All(e => e.Name == W + "tr");
                if (!rows && child.Name != W + "sdtContent")
                {
                    if (control != null) context.Loss("Template.TableControl", "Tagged non-row table wrappers retain cached cells and paragraphs but cannot execute safely.");
                    else context.Loss("Docx.TableStructure", "Table content-control and custom XML wrappers are flattened while retaining their cells and paragraphs.");
                }
                foreach (XElement nested in nestedContent)
                {
                    if (control != null && nested.Name == W + "tr")
                    {
                        RegisterRowControl(nested, control);
                    }
                    yield return nested;
                }
            }
            else yield return child;
        }
    }

    private TextStyle ResolveStyle(string? id, HashSet<string> visiting, bool characterStyle = false)
    {
        if (id == null)
        {
            TextStyle initial = characterStyle ? new TextStyle() : defaultRun.Copy(); initial.Alignment = defaultAlignment; initial.Layout = defaultLayout.Copy(); return initial;
        }

        if (!visiting.Add(id) || visiting.Count > context.Options.MaximumNestingDepth)
        {
            throw new InvalidDataException("Docx.StyleCycle: Styles contain a cycle or exceed the nesting limit.");
        }

        if (!styles.TryGetValue(id, out XElement? element))
        {
            context.Loss("Docx.MissingStyle", "A referenced style is missing; default text formatting is used.");
            TextStyle initial = characterStyle ? new TextStyle() : defaultRun.Copy(); initial.Alignment = defaultAlignment; initial.Layout = defaultLayout.Copy(); return initial;
        }

        TextStyle style = ResolveStyle((string?)element.Element(W + "basedOn")?.Attribute(W + "val"), visiting, characterStyle);
        style.Code |= id == "WordnetCode";
        style.CodeBlock |= id == "WordnetCodeBlock";
        ReadParagraphProperties(element.Element(W + "pPr"), style);
        ReadRunProperties(element.Element(W + "rPr"), style, knownCodeStyle: style.Code || style.CodeBlock, visiting: visiting);
        return style;
    }

    private void ReadParagraphProperties(XElement? properties, TextStyle style)
    {
        if (properties == null)
        {
            return;
        }

        foreach (XElement property in properties.Elements())
        {
            if (property.Name == W + "outlineLvl")
            {
                if (!int.TryParse((string?)property.Attribute(W + "val"), NumberStyles.None, CultureInfo.InvariantCulture, out int level)
                    || level < 0 || level > 9)
                {
                    throw new InvalidDataException("Docx.Outline: Invalid outline level.");
                }

                if (level > 5 && level != 9)
                {
                    context.Loss("Docx.HeadingLevel", "Heading levels greater than six are not supported in Markdown.");
                }

                style.Heading = level < 6 ? level + 1 : 0;
            }
            else if (property.Name == W + "jc")
            {
                string value = DocxPackage.RequiredAttribute(property, W + "val");
                switch (value)
                {
                    case "left": style.Alignment = ParagraphAlignment.Left; break;
                    case "center": style.Alignment = ParagraphAlignment.Center; break;
                    case "right": style.Alignment = ParagraphAlignment.Right; break;
                    case "both": style.Alignment = ParagraphAlignment.Justified; break;
                    case "distribute": style.Alignment = ParagraphAlignment.Distributed; break;
                    case "mediumKashida":
                    case "highKashida":
                    case "lowKashida":
                    case "thaiDistribute":
                    case "numTab":
                    case "start":
                    case "end":
                        context.Loss("Docx.Alignment", "Justified, distributed and direction-dependent alignment becomes left alignment.");
                        style.Alignment = ParagraphAlignment.Left; break;
                    default: throw new InvalidDataException("Docx.Alignment: Invalid paragraph justification value.");
                }
            }
            else if (property.Name == W + "bidi") style.RightToLeft = IsOn(property);
            else if (property.Name == W + "numPr")
            {
                if (property.Element(W + "numId") is XElement num) style.ListId = Integer(num, W + "val", 0, int.MaxValue);
                if (property.Element(W + "ilvl") is XElement lvl) style.ListLevel = Integer(lvl, W + "val", 0, 8);
                if (property.Elements().Any(e => e.Name != W + "numId" && e.Name != W + "ilvl")) context.Loss("Docx.ListRevision", "Numbering revisions are not retained.");
            }
            else if (ReadLayoutProperty(property, style.Layout)) { }
            else if (property.Name == W + "rPr") { /* Paragraph mark is resolved separately from text runs. */ }
            else if (property.Name != W + "pStyle" && property.Name != W + "sectPr")
            {
                context.Loss("Docx.ParagraphFormatting", "Paragraph formatting such as spacing, paragraph-mark styling or page controls is not retained.");
            }
        }
    }

    private void ReadRunProperties(XElement? properties, TextStyle style, bool knownCodeStyle = false, HashSet<string>? visiting = null)
    {
        if (properties == null)
        {
            return;
        }

        foreach (XElement property in properties.Elements().OrderBy(p => p.Name == W + "rStyle" ? 0 : 1))
        {
            if (property.Name == W + "b")
            {
                // Resolve each style's basedOn chain before combining paragraph and character styles.
                style.Bold = IsOn(property);
            }
            else if (property.Name == W + "i")
            {
                style.Italic = IsOn(property);
            }
            else if (property.Name == W + "rStyle")
            {
                TextStyle character = ResolveStyle((string?)property.Attribute(W + "val"), visiting ?? new HashSet<string>(StringComparer.Ordinal), characterStyle: true);
                style.Code |= character.Code;
                style.Bold ^= character.Bold;
                style.Italic ^= character.Italic;
                style.FontSize = character.FontSize ?? style.FontSize;
                if (character.Appearance.FontFamily != null) style.Appearance.FontFamily = character.Appearance.FontFamily;
                style.Appearance.Underline |= character.Appearance.Underline;
                style.Appearance.Strike |= character.Appearance.Strike;
                style.Appearance.Background = character.Appearance.Background ?? style.Appearance.Background;
                style.Appearance.VerticalAlignment = character.Appearance.VerticalAlignment ?? style.Appearance.VerticalAlignment;
                if (character.HasColor) { style.Color = character.Color; style.HasColor = true; }
            }
            else if (property.Name == W + "sz") style.FontSize = Integer(property, W + "val", 2, 3276) / 2.0;
            else if (property.Name == W + "rFonts" && !knownCodeStyle)
            {
                style.Appearance.FontFamily = (string?)property.Attribute(W + "ascii") ?? (string?)property.Attribute(W + "hAnsi") ?? style.Appearance.FontFamily;
                if (property.Attributes().Any(a => a.Name.LocalName.EndsWith("Theme", StringComparison.Ordinal)))
                    context.Loss("Docx.ThemeFont", "Theme fonts without a resolved family use the explicit family or output fallback.");
            }
            else if (property.Name == W + "u")
            {
                string value = (string?)property.Attribute(W + "val") ?? "single";
                style.Appearance.Underline = value != "none";
                if (value is not "none" and not "single") context.Loss("Docx.Underline", "Unsupported underline decoration uses a single line.");
            }
            else if (property.Name == W + "strike") style.Appearance.Strike = IsOn(property);
            else if (property.Name == W + "vertAlign")
            {
                string value = DocxPackage.RequiredAttribute(property, W + "val");
                if (value is not "baseline" and not "superscript" and not "subscript") throw new InvalidDataException("Docx.VerticalAlignment: Invalid run alignment.");
                style.Appearance.VerticalAlignment = value == "baseline" ? null : value;
            }
            else if (property.Name == W + "highlight")
            {
                string value = DocxPackage.RequiredAttribute(property, W + "val");
                style.Appearance.Background = value switch
                {
                    "none" => null,
                    "yellow" => "FFFF00",
                    "green" => "00FF00",
                    "cyan" => "00FFFF",
                    "magenta" => "FF00FF",
                    "blue" => "0000FF",
                    "red" => "FF0000",
                    "darkBlue" => "000080",
                    "darkCyan" => "008080",
                    "darkGreen" => "008000",
                    "darkMagenta" => "800080",
                    "darkRed" => "800000",
                    "darkYellow" => "808000",
                    "darkGray" => "808080",
                    "lightGray" => "C0C0C0",
                    "black" => "000000",
                    "white" => "FFFFFF",
                    _ => throw new InvalidDataException("Docx.Highlight: Invalid highlight color."),
                };
            }
            else if (property.Name == W + "shd")
            {
                string? fill = (string?)property.Attribute(W + "fill");
                if (fill is not null and not "auto")
                {
                    if (fill.Length != 6 || !uint.TryParse(fill, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _)) throw new InvalidDataException("Docx.Color: Invalid shading color.");
                    style.Appearance.Background = fill.ToUpperInvariant();
                }
                if ((string?)property.Attribute(W + "val") is string pattern && pattern is not "clear" and not "solid" and not "nil")
                    context.Loss("Docx.Shading", "Patterned shading uses the solid background color.");
            }
            else if (property.Name == W + "color")
            {
                string value = DocxPackage.RequiredAttribute(property, W + "val");
                if (value != "auto" && (value.Length != 6 || !uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _)))
                    throw new InvalidDataException("Docx.Color: Expected six RGB hexadecimal digits or auto.");
                style.Color = value == "auto" ? null : value.ToUpperInvariant();
                style.HasColor = true;
                if (property.Attributes().Any(a => !a.IsNamespaceDeclaration && a.Name != W + "val")) context.Loss("Docx.ThemeColor", "Theme color uses its stored RGB fallback.");
            }
            else if (property.Name == W + "lang")
                context.Report("Docx.LanguageMetadata", "Language/proofing metadata is not retained; Unicode text is preserved.");
            else if (!(knownCodeStyle && property.Name == W + "rFonts"))
            {
                context.Loss("Docx.RunFormatting", "Unsupported run properties such as font families and complex-script formatting are not retained.");
            }
        }
    }

    private static bool IsOn(XElement element)
    {
        string? value = (string?)element.Attribute(W + "val");
        if (value == null || IsTrue(value))
        {
            return true;
        }

        if (value == "false" || value == "0" || value == "off")
        {
            return false;
        }

        throw new InvalidDataException("Docx.Boolean: Invalid on/off property value.");
    }

    private static bool IsTrue(string? value) => value == "true" || value == "1" || value == "on";

    private sealed class TextStyle
    {
        internal bool? RightToLeft { get; set; }
        internal bool Bold { get; set; }
        internal bool Italic { get; set; }
        internal bool Code { get; set; }
        internal bool CodeBlock { get; set; }
        internal int Heading { get; set; }
        internal ParagraphAlignment Alignment { get; set; }
        internal double? FontSize { get; set; }
        internal string? Color { get; set; }
        internal bool HasColor { get; set; }
        internal int ListId { get; set; }
        internal int ListLevel { get; set; }
        internal ParagraphLayout Layout { get; set; } = new ParagraphLayout();
        internal RunAppearance Appearance { get; set; } = new RunAppearance();

        internal TextStyle Copy()
        {
            var copy = (TextStyle)MemberwiseClone();
            copy.Layout = Layout.Copy();
            copy.Appearance = Appearance.Copy();
            return copy;
        }
    }
}
