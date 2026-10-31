using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using WordNet.Formats;
using WordNet.Internal;
using WordNet.Model;

namespace WordNet;

/// <summary>An immutable object snapshot, valid for editing only at its originating session version.</summary>
public sealed class DocumentObject
{
    internal DocumentObject(Guid session, long version, DocumentObjectData data)
    { Session = session; Version = version; Data = data; }
    internal Guid Session { get; }
    internal long Version { get; }
    internal DocumentObjectData Data { get; }
    /// <summary>Gets a placement identity independent of package names and relationship IDs.</summary>
    public Guid Id => Data.Id;
    /// <summary>Gets immutable chart data, or null for an equation.</summary>
    public ChartDefinition? Chart => Data.Chart;
    /// <summary>Gets the immutable equation tree, or null for a chart.</summary>
    public EquationExpression? Equation => Data.Equation;
    /// <summary>Gets the chart width in points; zero for equations whose dimensions depend on the math font.</summary>
    public double Width => Data.Width;
    /// <summary>Gets the chart height in points; zero for equations whose dimensions depend on the math font.</summary>
    public double Height => Data.Height;
    /// <summary>Gets the separate textual description.</summary>
    public string Description => Data.Description;
    /// <summary>Gets whether an equation is displayed on its own line.</summary>
    public bool Display => Data.Display;
    /// <summary>Gets the equation size in points.</summary>
    public double FontSize => Data.FontSize;
}

public sealed partial class WordDocument
{
    /// <summary>Gets immutable body/cell object snapshots. Every successful edit invalidates prior handles.</summary>
    public IReadOnlyList<DocumentObject> Objects
    { get { lock (gate) return model.BodyParagraphs.SelectMany(p => p.Runs).Where(r => r.Object != null).Select(r => new DocumentObject(sessionId, version, r.Object!)).ToList().AsReadOnly(); } }

    /// <summary>Appends a chart paragraph with real editable chart caches and a plain-value embedded workbook.</summary>
    public DocumentObject AddChart(ChartDefinition definition, double width = 300, double height = 180, string description = "", CancellationToken cancellationToken = default)
    {
        Guard.NotNull(definition, nameof(definition)); Guid id = Guid.NewGuid();
        lock (gate)
        {
            _ = Edit((m, c) => { AddObject(m, new DocumentObjectData(definition, ChartObjectFormat.Create(definition, c), width, height, description, id)); return 1; }, cancellationToken, objectAware: true, controlAware: true);
            return ObjectSnapshot(id);
        }
    }

    /// <summary>Replaces one chart definition, rebuilding caches and workbook together. Stale or foreign handles fail atomically.</summary>
    public DocumentObject UpdateChart(DocumentObject handle, ChartDefinition definition, CancellationToken cancellationToken = default)
        => UpdateChart(handle, definition, null, cancellationToken);

    /// <summary>Replaces chart data and optionally its textual description; null retains the current description and empty clears it.</summary>
    public DocumentObject UpdateChart(DocumentObject handle, ChartDefinition definition, string? description, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(definition, nameof(definition));
        return ChangeObject(handle, (old, c) => old.Chart == null ? throw new ArgumentException("Object.Kind: A chart handle is required.", nameof(handle))
            : new DocumentObjectData(definition, ChartObjectFormat.Create(definition, c), old.Width, old.Height, description ?? old.Description, old.Id), cancellationToken);
    }

    /// <summary>Appends genuine OMML as an equation paragraph. Rendering separately requires a qualified caller math font.</summary>
    public DocumentObject AddEquation(EquationExpression expression, bool display = true, double fontSize = 12, string description = "", CancellationToken cancellationToken = default)
    {
        Guard.NotNull(expression, nameof(expression)); Guid id = Guid.NewGuid();
        lock (gate)
        {
            _ = Edit((m, c) => { AddObject(m, EquationData(expression, display, fontSize, description, id, c)); return 1; }, cancellationToken, objectAware: true, controlAware: true);
            return ObjectSnapshot(id);
        }
    }

    /// <summary>Replaces an equation tree, retaining its size, display mode, identity and textual description.</summary>
    public DocumentObject UpdateEquation(DocumentObject handle, EquationExpression expression, CancellationToken cancellationToken = default)
        => UpdateEquation(handle, expression, null, cancellationToken);

    /// <summary>Replaces an equation tree and optionally its textual description; null retains the current description and empty clears it.</summary>
    public DocumentObject UpdateEquation(DocumentObject handle, EquationExpression expression, string? description, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(expression, nameof(expression));
        return ChangeObject(handle, (old, c) => old.Equation == null ? throw new ArgumentException("Object.Kind: An equation handle is required.", nameof(handle))
            : EquationData(expression, old.Display, old.FontSize, description ?? old.Description, old.Id, c), cancellationToken);
    }

    /// <summary>Removes an entire placement and its privately retained graph, including embedded workbook data.</summary>
    public DocumentEditResult RemoveObject(DocumentObject handle, CancellationToken cancellationToken = default) => Edit((m, c) =>
    {
        TextRun run = FindObject(m, handle);
        foreach (ParagraphBlock p in m.BodyParagraphs)
            if (p.Runs.Remove(run))
            {
                if (p.RemoveWhenObjectEmpty && p.Runs.Count == 0) m.Blocks.Remove(p);
                return 1;
            }
        throw new ArgumentException("Object.Missing: Placement is unavailable.", nameof(handle));
    }, cancellationToken, objectAware: true, controlAware: true);

    private DocumentObject ChangeObject(DocumentObject handle, Func<DocumentObjectData, ConversionContext, DocumentObjectData> change, CancellationToken token)
    {
        lock (gate)
        {
            _ = Edit((m, c) =>
            {
                TextRun old = FindObject(m, handle); DocumentObjectData replacement = change(old.Object!, c);
                foreach (ParagraphBlock p in m.BodyParagraphs)
                { int i = p.Runs.IndexOf(old); if (i >= 0) { p.Runs[i] = new TextRun("", retainedObject: replacement); return 1; } }
                throw new ArgumentException("Object.Missing: Placement is unavailable.", nameof(handle));
            }, token, objectAware: true, controlAware: true);
            return ObjectSnapshot(handle.Id);
        }
    }
    private TextRun FindObject(DocumentModel m, DocumentObject handle)
    {
        Guard.NotNull(handle, nameof(handle));
        if (handle.Session != sessionId || handle.Version != version) throw new ArgumentException("Object.StaleHandle: Foreign or stale object handle.", nameof(handle));
        return m.BodyParagraphs.SelectMany(p => p.Runs).SingleOrDefault(r => r.Object?.Id == handle.Id) ?? throw new ArgumentException("Object.Missing: Placement is unavailable.", nameof(handle));
    }
    private DocumentObject ObjectSnapshot(Guid id) => new DocumentObject(sessionId, version, model.BodyParagraphs.SelectMany(p => p.Runs).Single(r => r.Object?.Id == id).Object!);
    private static void AddObject(DocumentModel m, DocumentObjectData data)
    {
        var p = new ParagraphBlock { RemoveWhenObjectEmpty = true, Section = m.FinalSection ?? m.Blocks.LastOrDefault()?.Section };
        p.Runs.Add(new TextRun("", retainedObject: data)); m.Blocks.Add(p);
    }
    private static DocumentObjectData EquationData(EquationExpression expression, bool display, double size, string description, Guid id, ConversionContext c)
    {
        var data = new DocumentObjectData(expression, display, size, description, id);
        // Public trees must also fit the serialized OMML depth and operation budgets.
        _ = MathObjectFormat.Read(MathObjectFormat.Write(expression, display, size), c);
        return data;
    }
}
