using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using WordNet.Formats;
using WordNet.Internal;
using WordNet.Model;

namespace WordNet;

public sealed partial class WordDocument
{
    private IReadOnlyList<DocumentContentControl>? controlSnapshots;
    private long controlSnapshotsVersion = -1;
    /// <summary>Loads the bounded typed DOCX control profile with the default explicit-UTC date policy, leaving the input open.</summary>
    public static WordDocument LoadWithControls(Stream source, out ConversionResult result, ConversionOptions? options = null, CancellationToken cancellationToken = default) =>
        LoadWithControls(source, new ContentControlLoadOptions(), out result, options, cancellationToken);

    /// <summary>Loads typed DOCX controls and local binding metadata without executing tags or synchronizing XML. Options are captured once.</summary>
    public static WordDocument LoadWithControls(Stream source, ContentControlLoadOptions controlOptions, out ConversionResult result,
        ConversionOptions? options = null, CancellationToken cancellationToken = default)
        => LoadWithControls(source, DocumentFormat.Docx, controlOptions, out result, options, cancellationToken);

    /// <summary>Loads typed controls from a macro-free DOCX or DOTX package without executing tags or synchronizing XML.</summary>
    public static WordDocument LoadWithControls(Stream source, DocumentFormat format, ContentControlLoadOptions controlOptions, out ConversionResult result,
        ConversionOptions? options = null, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(source, nameof(source)); Guard.NotNull(controlOptions, nameof(controlOptions));
        if (format is not DocumentFormat.Docx and not DocumentFormat.Dotx) throw new NotSupportedException("Control.InputFormat: Typed controls require DOCX or DOTX.");
        if (!source.CanRead) throw new ArgumentException("The source must be readable.", nameof(source));
        ContentControlDateMode mode = controlOptions.DateMode;
        if (mode is not ContentControlDateMode.UtcInstant and not ContentControlDateMode.DateOnly) throw new ArgumentOutOfRangeException(nameof(controlOptions));
        options ??= new ConversionOptions();
        var c = new ConversionContext(options, cancellationToken) { Editing = true, PreserveControls = true, ControlDateMode = mode, Destination = DocumentFormat.Docx };
        c.CheckCancellation();
        using var input = new BoundedMemoryStream(options.MaximumInputBytes); Transfer(source, input, c, options.MaximumInputBytes); input.Position = 0;
        DocumentModel loaded = DocxReader.Read(input, c, format == DocumentFormat.Dotx); Admit(loaded, c); result = c.Result(0);
        return new WordDocument(loaded, options) { diagnostics = result.Diagnostics };
    }
    /// <summary>Gets detached typed-control snapshots in source order, cached until a successful edit invalidates their mutation handles.</summary>
    public IReadOnlyList<DocumentContentControl> ContentControls
    {
        get
        {
            lock (gate)
            {
                if (controlSnapshots != null && controlSnapshotsVersion == version) return controlSnapshots;
                if (!model.Controls.HasControls) return Array.Empty<DocumentContentControl>();
                var c = new ConversionContext(options, CancellationToken.None); var owners = ControlOwners.Index(model, c);
                controlSnapshots = new ReadOnlyCollection<DocumentContentControl>(model.Controls.Items.Values.Select(d => new DocumentContentControl(sessionId, version, d, owners[d.Id].ParentId, owners[d.Id].Story, owners[d.Id].Structure, ControlContent(owners[d.Id], c))).ToList());
                controlSnapshotsVersion = version;
                return controlSnapshots;
            }
        }
    }
    private ReadOnlyCollection<ContentControlParagraph> ControlContent(ControlOwner owner, ConversionContext c)
    {
        var content = new List<ContentControlParagraph>();
        foreach (ParagraphBlock p in owner.Content)
        {
            c.CheckCancellation();
            IEnumerable<TextRun> runs = owner.Paragraph == null ? p.Runs : owner.Runs;
            // Only a formatting shell and admitted run values escape; no section/story/model references.
            var shell = new ParagraphBlock
            {
                StyleId = p.StyleId,
                FallbackFontSize = p.FallbackFontSize,
                FallbackAfter = p.FallbackAfter,
                Mark = p.Mark == null ? null : FormatCopy(p.Mark),
                Alignment = p.Alignment,
                RightToLeft = p.RightToLeft,
                HeadingLevel = p.HeadingLevel,
                Layout = p.Layout.Copy()
            };
            var snapshot = new ReadOnlyCollection<DocumentText>(runs.Select(Snapshot).ToList());
            content.Add(new ContentControlParagraph(shell, snapshot) { Origin = sessionId });
        }
        return new ReadOnlyCollection<ContentControlParagraph>(content);
    }
    private static TextRun FormatCopy(TextRun r) => new TextRun("", r.Bold, r.Italic, r.Code, fontSize: r.FontSize, color: r.Color, appearance: r.Appearance.Copy());
    private ControlData ControlHandle(DocumentModel m, DocumentContentControl handle, bool detach = false)
    {
        Guard.NotNull(handle, nameof(handle));
        if (handle.Session != sessionId || handle.Version != version || !m.Controls.Items.TryGetValue(handle.Id, out ControlData? data) || data.Identity != handle.Identity)
            throw new ArgumentException("Control.StaleHandle: Reacquire a live control handle from its originating session.", nameof(handle));
        if (data.Lock is ContentControlLock.ContentLocked or ContentControlLock.ControlAndContentLocked || detach && data.Lock != ContentControlLock.Unlocked)
            throw new InvalidOperationException("Control.Locked: The editing lock rejects this mutation.");
        return data;
    }
    private DocumentEditResult ChangeControl(DocumentContentControl handle, Action<DocumentModel, ControlData, ControlOwner, ConversionContext> change, CancellationToken token)
    {
        Guard.NotNull(handle, nameof(handle));
        return Edit((m, c) =>
        {
            ControlData data = ControlHandle(m, handle); var owners = ControlOwners.Index(m, c); ControlOwner owner = owners[data.Id];
            CheckNestedControlEdit(m, owner);
            using var location = c.At(owner.Content.First().Location);
            change(m, data, owner, c);
            if (owner.ParentId is int parent) ControlValues.Infer(m.Controls.Items[parent], owners[parent], c);
            ControlOwners.Normalize(m); return 1;
        }, token, controlAware: true);
    }
    /// <summary>Atomically sets a checkbox value and clears placeholder display. Content locks and stale handles reject.</summary>
    public DocumentEditResult SetControlChecked(DocumentContentControl handle, bool value, CancellationToken cancellationToken = default) =>
        ChangeControl(handle, (_, d, owner, c) => ControlValues.SetChecked(d, owner, value, c), cancellationToken);
    /// <summary>Selects an exact ordinal stored value. Null clears selection without inventing a placeholder.</summary>
    public DocumentEditResult SetControlSelection(DocumentContentControl handle, string? storedValue, CancellationToken cancellationToken = default) =>
        ChangeControl(handle, (_, d, owner, c) => ControlValues.SetSelection(d, owner, storedValue, c), cancellationToken);
    /// <summary>Sets bounded literal combo text. Item-label or bound-member collisions require the selection API; empty clears selection.</summary>
    public DocumentEditResult SetControlComboText(DocumentContentControl handle, string text, CancellationToken cancellationToken = default)
    { Guard.NotNull(text, nameof(text)); return ChangeControl(handle, (_, d, owner, c) => ControlValues.SetCombo(d, owner, text, c), cancellationToken); }
    /// <summary>Sets an explicit date matching the captured session interpretation. Null clears fullDate and display.</summary>
    public DocumentEditResult SetControlDate(DocumentContentControl handle, ContentControlDateValue? value, CancellationToken cancellationToken = default) =>
        ChangeControl(handle, (m, d, owner, c) => ControlValues.SetDate(d, owner, value, m.Controls.DateMode, c), cancellationToken);
    /// <summary>Sets literal plain text. LF soft breaks require the source multiLine setting; markup is never interpreted.</summary>
    public DocumentEditResult SetControlText(DocumentContentControl handle, string text, CancellationToken cancellationToken = default)
    { Guard.NotNull(text, nameof(text)); return ChangeControl(handle, (_, d, owner, c) => ControlValues.SetPlain(d, owner, text, c), cancellationToken); }

    /// <summary>Atomically removes a typed wrapper, retaining its current text content by default. When content is deleted, a block owner retains an empty paragraph shell.</summary>
    /// <remarks>Wrapper locks always reject removal; a content lock also rejects deleting content. Nested parents with children, dirty bindings and removal of the last control while local XML/glossary resources remain are rejected.</remarks>
    public DocumentEditResult RemoveControl(DocumentContentControl handle, bool preserveContent = true, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(handle, nameof(handle));
        return Edit((m, c) =>
        {
            if (handle.Session != sessionId || handle.Version != version || !m.Controls.Items.TryGetValue(handle.Id, out ControlData? data) || data.Identity != handle.Identity)
                throw new ArgumentException("Control.StaleHandle: Reacquire a live control handle from its originating session.", nameof(handle));
            if (data.Lock is ContentControlLock.ControlLocked or ContentControlLock.ControlAndContentLocked
                || !preserveContent && data.Lock == ContentControlLock.ContentLocked)
                throw new InvalidOperationException("Control.Locked: The editing lock rejects this removal.");
            if (data.BindingState == ContentControlBindingState.Dirty)
                throw new InvalidOperationException("Control.BindingDirty: Explicitly apply, write back or detach an edited binding before removing its wrapper.");
            if (m.Controls.Items.Count == 1 && (m.Controls.XmlStores.Count != 0 || m.Controls.Glossary != null
                || m.Controls.GlossaryResources.Count != 0 || m.Controls.GlossaryContent.Count != 0))
                throw new NotSupportedException("Control.Resources: The last control cannot be removed while retained local XML or glossary resources remain.");
            var owners = ControlOwners.Index(m, c); ControlOwner owner = owners[data.Id];
            owner.Refresh(data.Id, c); CheckNestedControlEdit(m, owner);
            if (owner.Paragraph != null)
            {
                if (preserveContent) foreach (TextRun run in owner.Runs) run.TypedControlId = null;
                else owner.Paragraph.Runs.RemoveRange(owner.Start, owner.Count);
            }
            else if (preserveContent)
            {
                foreach (ParagraphBlock paragraph in owner.Content) paragraph.TypedControlId = null;
            }
            else
            {
                DocumentBlock first = owner.Blocks[owner.Start];
                var shell = new ParagraphBlock { Section = first.Section, Location = first.Location, FallbackFontSize = 11, FallbackAfter = 0 };
                owner.Blocks.RemoveRange(owner.Start, owner.Count); owner.Blocks.Insert(owner.Start, shell);
            }
            m.Controls.Items.Remove(data.Id);
            if (owner.ParentId is int parent) ControlValues.Infer(m.Controls.Items[parent], owners[parent], c);
            ControlOwners.Normalize(m); return 1;
        }, cancellationToken, controlAware: true);
    }
    /// <summary>Replaces inline rich content with copied formatted text. Images, fields, boundaries and structural breaks are rejected.</summary>
    public DocumentEditResult ReplaceControlContent(DocumentContentControl handle, IEnumerable<DocumentText> runs, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(runs, nameof(runs));
        return ChangeControl(handle, (_, d, owner, c) =>
        {
            ControlValues.Require(d, ContentControlKind.RichText);
            if (!d.Inline) throw new ArgumentException("Control.Owner: Use ReplaceControlParagraphs for block rich controls.", nameof(handle));
            var budget = new ControlInputBudget(c); var replacement = new List<TextRun>(); AddRuns(replacement, budget.Runs(runs), c);
            owner.SetRuns(d.Id, replacement); ControlValues.ClearPlaceholder(d); d.State = replacement.All(r => r.Text.Length == 0) ? ContentControlValueState.Empty : ContentControlValueState.Value; ControlValues.Dirty(d);
        }, cancellationToken);
    }
    /// <summary>Replaces block rich content with copied ordinary paragraphs. Empty input retains an empty value paragraph.</summary>
    public DocumentEditResult ReplaceControlParagraphs(DocumentContentControl handle, IEnumerable<ContentControlParagraph> paragraphs, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(paragraphs, nameof(paragraphs));
        return ChangeControl(handle, (_, d, owner, c) =>
        {
            ControlValues.Require(d, ContentControlKind.RichText);
            if (d.Inline) throw new ArgumentException("Control.Owner: Inline controls cannot receive block paragraphs.", nameof(handle));
            var replacement = new List<ParagraphBlock>(); var budget = new ControlInputBudget(c);
            foreach (ContentControlParagraph input in paragraphs)
            {
                c.CheckCancellation(); budget.Node();
                if (input == null) throw new ArgumentException("Control.Content: Paragraphs cannot be null.", nameof(paragraphs));
                var p = new ParagraphBlock
                {
                    Alignment = input.Data.Alignment,
                    HeadingLevel = input.Data.HeadingLevel,
                    RightToLeft = input.Data.RightToLeft,
                    Layout = input.Data.Layout.Copy(),
                    Mark = input.Data.Mark == null ? null : FormatCopy(input.Data.Mark),
                    FallbackFontSize = input.Data.FallbackFontSize ?? (input.Data.HeadingLevel == 0 ? 11 : (double?)null),
                    FallbackAfter = input.Data.FallbackAfter ?? 0,
                    StyleId = input.Origin == sessionId ? input.Data.StyleId : null
                };
                if (input.Data.StyleId != null && input.Origin != sessionId) c.Report("Control.StyleMaterialized", "Foreign paragraph snapshots retain resolved appearance/heading role without importing style identities.");
                AddRuns(p.Runs, budget.Runs(input.Runs), c); replacement.Add(p);
            }
            owner.SetParagraphs(d.Id, replacement); ControlValues.ClearPlaceholder(d);
            d.State = replacement.All(p => p.Text.Length == 0) ? ContentControlValueState.Empty : ContentControlValueState.Value; ControlValues.Dirty(d);
        }, cancellationToken);
    }
    /// <summary>Restores preserved authored placeholder content. It never fetches or invents a global placeholder.</summary>
    public DocumentEditResult ShowControlPlaceholder(DocumentContentControl handle, CancellationToken cancellationToken = default) =>
        ChangeControl(handle, (_, d, owner, c) =>
        {
            if (d.PlaceholderContent.Count == 0) throw new NotSupportedException("Control.Placeholder: No admitted authored placeholder is available.");
            string text = string.Join("\n", d.PlaceholderContent.Select(p => p.Text));
            if (d.Kind == ContentControlKind.Checkbox && text.Length != 1 || d.Kind != ContentControlKind.RichText && d.PlaceholderContent.Count != 1)
                throw new NotSupportedException("Control.Placeholder: The authored placeholder cannot fit this control kind.");
            var cloner = new DocumentCloner(c); var paragraphs = d.PlaceholderContent.Select(p => (ParagraphBlock)cloner.Block(p)).ToArray();
            if (d.Inline) owner.SetRuns(d.Id, paragraphs.SelectMany(p => p.Runs)); else owner.SetParagraphs(d.Id, paragraphs);
            d.Placeholder = true; d.State = ContentControlValueState.Placeholder; d.SelectedValue = null; d.FreeText = null;
            d.DateValue = null; d.Properties.Element(ControlValues.W + "date")?.Attribute(ControlValues.W + "fullDate")?.Remove();
            d.Properties.SetElementValue(ControlValues.W + "showingPlcHdr", ""); ControlValues.Dirty(d);
        }, cancellationToken);
    /// <summary>Returns a detached static-display session with typed wrappers/tags/bindings removed; the original and locks stay unchanged.</summary>
    public WordDocument CreateControlDisplayView(out ConversionResult result, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            using var operation = BeginOperation(); var c = Context(options, cancellationToken);
            DocumentModel copy = new DocumentCloner(c).Copy(model);
            if (copy.Controls.HasControls)
            {
                ControlDisplay.Flatten(copy, c);
                c.Report("Control.OutputLoss", "An explicit detached display view removed typed controls, tags, locks and local binding data.");
            }
            Admit(copy, c); c.CheckCancellation(); result = c.Result(0);
            return new WordDocument(copy, options) { diagnostics = result.Diagnostics };
        }
    }
    /// <summary>Explicitly applies existing bounded local XML bindings to selected control values in one atomic transaction.</summary>
    public DocumentEditResult ApplyControlBindings(IEnumerable<DocumentContentControl> handles, CancellationToken cancellationToken = default) => ChangeBindings(handles, 0, cancellationToken);
    /// <summary>Explicitly writes concrete values into existing local XML targets. All sharing controls must be selected and agree.</summary>
    public DocumentEditResult WriteBackControlBindings(IEnumerable<DocumentContentControl> handles, CancellationToken cancellationToken = default) => ChangeBindings(handles, 1, cancellationToken);
    /// <summary>Explicitly removes existing bindings from unlocked selected controls, retaining their display and other metadata.</summary>
    public DocumentEditResult DetachControlBindings(IEnumerable<DocumentContentControl> handles, CancellationToken cancellationToken = default) => ChangeBindings(handles, 2, cancellationToken);
    private DocumentEditResult ChangeBindings(IEnumerable<DocumentContentControl> handles, int direction, CancellationToken token)
    {
        Guard.NotNull(handles, nameof(handles));
        return Edit((m, c) =>
        {
            var selected = new List<ControlData>(); var ids = new HashSet<int>();
            foreach (DocumentContentControl handle in handles)
            {
                c.ReserveControlWork(1);
                if (selected.Count == 1024) throw new InvalidDataException("Limit.Controls: Too many selected bindings.");
                ControlData data = ControlHandle(m, handle, direction == 2);
                if (!ids.Add(data.Id)) throw new ArgumentException("Control.Selection: Duplicate binding handles.", nameof(handles));
                if (data.Binding == null) throw new ArgumentException("Binding.Store: The selected control has no binding.", nameof(handles));
                selected.Add(data);
            }
            var owners = ControlOwners.Index(m, c);
            foreach (ControlData data in selected) CheckNestedControlEdit(m, owners[data.Id]);
            if (direction == 0) foreach (ControlData data in selected) ControlBindings.Apply(data, owners[data.Id], m.Controls, c);
            else if (direction == 1) ControlBindings.WriteBack(m, selected, owners, c);
            else foreach (ControlData data in selected) { data.Binding = null; data.BindingState = ContentControlBindingState.None; data.Properties.Elements(ControlValues.W + "dataBinding").Remove(); }
            foreach (int parent in selected.Select(data => owners[data.Id].ParentId).Where(id => id.HasValue).Select(id => id!.Value).Distinct())
                ControlValues.Infer(m.Controls.Items[parent], owners[parent], c);
            ControlOwners.Normalize(m); return selected.Count;
        }, token, controlAware: true);
    }
    private static void CheckNestedControlEdit(DocumentModel model, ControlOwner owner)
    {
        if (owner.Structure != ControlStructure.None) throw new NotSupportedException("Control.ProtectedOwner: Structural wrappers cannot be edited or removed through paragraph operations.");
        if (owner.HasChildren) throw new NotSupportedException("Control.ProtectedOwner: A parent containing typed children cannot be replaced or rebound.");
        if (owner.ParentId is int parent && model.Controls.Items[parent].Lock is ContentControlLock.ContentLocked or ContentControlLock.ControlAndContentLocked)
            throw new InvalidOperationException("Control.Locked: The parent content lock rejects this child mutation.");
    }
    private sealed class ControlInputBudget
    {
        private readonly ConversionContext context; private long characters; private int nodes;
        internal ControlInputBudget(ConversionContext context) { this.context = context; }
        internal void Node()
        { context.ReserveControlWork(1); nodes = checked(nodes + 1); Check(); }
        private void Check()
        { if (characters > context.Options.MaximumCharacters || characters * 2 + nodes * 128L > context.Options.MaximumExpandedBytes) throw new InvalidDataException("Limit.Controls: Aggregate replacement content exceeds resource limits."); }
        internal IEnumerable<DocumentText> Runs(IEnumerable<DocumentText> runs)
        {
            foreach (DocumentText value in runs)
            {
                Node();
                if (value == null) throw new ArgumentException("Control.Content: Text values cannot be null.", nameof(runs));
                if (value.Image != null || value.Text.Contains('\f') || value.Text.Contains('\v')) throw new NotSupportedException("Control.ProtectedOwner: Rich controls accept text and soft breaks only, without images or structural breaks.");
                characters = checked(characters + value.Text.Length + (value.Formatting.FontFamily?.Length ?? 0)); Check();
                ControlValues.CheckText(value.Text, context); yield return value;
            }
        }
    }
}
