using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Xml;
using WordNet.Internal;

namespace WordNet.Model;

internal sealed class ObjectPart
{
    private readonly byte[] bytes;
    internal ObjectPart(string name, string contentType, byte[] value)
    {
        Name = name; ContentType = contentType; bytes = (byte[])value.Clone();
        Hash = Digest(bytes);
    }
    internal string Name { get; }
    internal string ContentType { get; }
    internal string Hash { get; }
    internal int Length => bytes.Length;
    internal byte[] Read() => (byte[])bytes.Clone();
    internal static string Digest(byte[] value)
    {
#if NET8_0_OR_GREATER
        return Convert.ToBase64String(SHA256.HashData(value));
#else
        using var hash = SHA256.Create(); return Convert.ToBase64String(hash.ComputeHash(value));
#endif
    }
}

internal sealed class ObjectGraph
{
    internal ObjectGraph(string root, IEnumerable<ObjectPart> parts, long nestedExpansion, int nestedEntries = 0, long nestedCharacters = 0)
    {
        Root = root; Parts = parts.ToList().AsReadOnly(); NestedExpansion = nestedExpansion; NestedEntries = nestedEntries; NestedCharacters = nestedCharacters;
        if (Parts.Count is < 1 or > 32 || Parts.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Parts.Count
            || !Parts.Any(p => p.Name == root)) throw new InvalidDataException("Object.Graph: Invalid admitted part closure.");
        string key = string.Join("\n", Parts.OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => p.Name + ":" + p.ContentType + ":" + p.Hash));
        Identity = ObjectPart.Digest(System.Text.Encoding.UTF8.GetBytes(key));
    }
    internal string Root { get; }
    internal IReadOnlyList<ObjectPart> Parts { get; }
    internal long NestedExpansion { get; }
    internal int NestedEntries { get; }
    internal long NestedCharacters { get; }
    internal long Bytes => Parts.Sum(p => (long)p.Length);
    internal string Identity { get; }
}

internal sealed class DocumentObjectData
{
    internal DocumentObjectData(ChartDefinition chart, ObjectGraph graph, double width, double height, string description, Guid? id = null, bool renderable = true)
    { Chart = chart; Graph = graph; Width = width; Height = height; Description = description; Id = id ?? Guid.NewGuid(); Renderable = renderable; Validate(); }
    internal DocumentObjectData(EquationExpression equation, bool display, double size, string description, Guid? id = null, string? retainedXml = null, bool renderable = true)
    { Equation = equation; Display = display; FontSize = size; Description = description; Id = id ?? Guid.NewGuid(); EquationXml = retainedXml; Renderable = renderable; Validate(); }
    internal Guid Id { get; }
    internal ChartDefinition? Chart { get; }
    internal ObjectGraph? Graph { get; }
    internal EquationExpression? Equation { get; }
    internal string? EquationXml { get; }
    internal bool Display { get; }
    internal double Width { get; }
    internal double Height { get; }
    internal double FontSize { get; } = 12;
    internal string Description { get; }
    internal bool Renderable { get; } = true;
    internal DocumentObjectData Remap() => Chart != null
        ? new DocumentObjectData(Chart, Graph!, Width, Height, Description, renderable: Renderable)
        : new DocumentObjectData(Equation!, Display, FontSize, Description, retainedXml: EquationXml, renderable: Renderable);
    private void Validate()
    {
        ObjectText.Check(Description, nameof(Description), 4096);
        if (Chart != null && (!(Width >= 36 && Width <= 1440) || !(Height >= 36 && Height <= 1440))) throw new InvalidDataException("Chart.Extent: Chart dimensions must be 36–1440 points.");
        if (Equation != null && !(FontSize >= 4 && FontSize <= 144)) throw new InvalidDataException("Equation.Size: Math size must be 4–144 points.");
    }
}

internal sealed class ObjectBudget
{
    private readonly ConversionContext context;
    private readonly HashSet<string> graphs = new HashSet<string>(StringComparer.Ordinal);
    private int placements;
    internal int Placements => placements;
    private long points, cells, mathNodes, characters, bytes, renderedCharacters, renderedWork, nestedEntries, modelEntries, xmlNodes, xmlCharacters;
    internal ObjectBudget(ConversionContext context) { this.context = context; }
    internal void Place(DocumentObjectData item)
    {
        context.CheckCancellation();
        if (++placements > 128) throw new InvalidDataException("Object.Placements: At most 128 object placements are admitted.");
        if (item.Chart is ChartDefinition chart)
        {
            points = checked(points + (long)chart.Categories.Count * chart.Series.Count);
            // Shared immutable graphs charge storage once; every placement is still counted.
            if (graphs.Add(item.Graph!.Identity))
            {
                characters = checked(characters + chart.Title.Length + chart.CategoryTitle.Length + chart.ValueTitle.Length + chart.Categories.Sum(s => (long)s.Length) + chart.Series.Sum(s => (long)s.Name.Length) + item.Graph.NestedCharacters);
                bytes = checked(bytes + item.Graph.NestedExpansion + (context.DeclaredExpandedBytes.HasValue ? 0 : item.Graph.Bytes));
                if (!context.ArchiveEntryCount.HasValue) modelEntries = checked(modelEntries + item.Graph.Parts.Count + item.Graph.NestedEntries);
            }
        }
        else
        {
            mathNodes = checked(mathNodes + item.Equation!.NodeCount); characters = checked(characters + item.Equation.Characters);
            bytes = checked(bytes + (item.EquationXml?.Length ?? 0) * 2L);
        }
        Finish(0, 0);
    }
    internal void Workbook(long count, long expansion, long currentCharacters = 0)
    {
        cells = checked(cells + count);
        if (cells > 100000 || currentCharacters > context.Options.MaximumCharacters - characters || expansion > context.Options.MaximumExpandedBytes - (context.DeclaredExpandedBytes ?? 0) - bytes)
            throw new InvalidDataException("Object.WorkbookLimit: Nested worksheet data exceeds its operation allowance.");
    }
    internal void Archive(int count)
    {
        nestedEntries = checked(nestedEntries + count);
        if (nestedEntries + (context.ArchiveEntryCount ?? 0) > context.Options.MaximumArchiveEntries)
            throw new InvalidDataException("Object.ArchiveLimit: Outer and nested package entries exceed their shared operation allowance.");
    }
    internal (int Nodes, long Characters) ScanXml(XmlReader reader)
    {
        context.CheckCancellation(); int count = 0; long text = 0;
        if (reader.NodeType == XmlNodeType.Element)
        {
            count = 1 + reader.AttributeCount; text = reader.Name.Length;
            if (reader.HasAttributes) { while (reader.MoveToNextAttribute()) text = checked(text + reader.Name.Length + reader.Value.Length); reader.MoveToElement(); }
        }
        else if (reader.NodeType is XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace or XmlNodeType.Comment or XmlNodeType.ProcessingInstruction)
        { count = 1; text = reader.Value.Length; }
        xmlNodes = checked(xmlNodes + count); xmlCharacters = checked(xmlCharacters + text);
        if (xmlCharacters > context.Options.MaximumCharacters || xmlNodes * 64 + xmlCharacters * 2 + bytes + (context.DeclaredExpandedBytes ?? 0) > context.Options.MaximumExpandedBytes)
            throw new InvalidDataException("Object.XmlLimit: Aggregate object XML exceeds its allowance before tree allocation.");
        return (count, text);
    }
    internal void Render(long text, long work)
    {
        context.CheckCancellation(); renderedCharacters = checked(renderedCharacters + text); renderedWork = checked(renderedWork + work);
        if (renderedCharacters > context.Options.MaximumCharacters || renderedCharacters * 2 + renderedWork * 128 > context.Options.MaximumExpandedBytes)
            throw new InvalidDataException("Object.LayoutLimit: Aggregate object rendering work exceeds its operation allowance.");
    }
    internal void Finish(long text, long modelBytes)
    {
        if (points > 100000 || mathNodes > 10000 || characters + text > context.Options.MaximumCharacters || !context.ArchiveEntryCount.HasValue && modelEntries + 8 > context.Options.MaximumArchiveEntries
            || bytes + modelBytes + (context.DeclaredExpandedBytes ?? 0) + mathNodes * 128 + characters * 2 > context.Options.MaximumExpandedBytes)
            throw new InvalidDataException("Object.ResourceLimit: Aggregate object/model resources exceed their allowance.");
    }
}

internal static class ObjectSafety
{
    internal static void Owners(DocumentModel model)
    {
        if (model.ObjectCount == 0) return;
        var body = new HashSet<ParagraphBlock>(ReviewStore.SupportedParagraphs(model.Blocks)); var identifiers = new HashSet<Guid>();
        foreach (ParagraphBlock paragraph in model.Paragraphs)
        {
            var comments = new HashSet<string>(StringComparer.Ordinal);
            foreach (TextRun run in paragraph.Runs)
            {
                if (run.Comment != null) { if (run.CommentBoundary == 1) comments.Add(run.Comment); else if (run.CommentBoundary == 2) comments.Remove(run.Comment); }
                if (run.Object == null) continue;
                if (!body.Contains(paragraph) || paragraph.Control != null || run.Control != null || run.Revision != null || run.Comment != null
                    || run.FieldInstruction != null || run.Bookmark != null || run.Note != null || run.Box != null || run.Image != null || run.Link != null || comments.Count != 0)
                    throw new NotSupportedException("Object.Owner: Retained objects require ordinary body/cell paragraph owners without controls, fields, history or links.");
                if (!identifiers.Add(run.Object!.Id)) throw new InvalidDataException("Object.Identity: Object placement identifiers must be unique.");
            }
        }
    }
}
