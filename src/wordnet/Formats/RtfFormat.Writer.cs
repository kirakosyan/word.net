using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using WordNet.Internal;
using WordNet.Model;

namespace WordNet.Formats;

internal static partial class RtfFormat
{
    internal static void Write(DocumentModel model, Stream output, ConversionContext context)
    {
        var fonts = new List<string> { "Noto Sans" };
        var colors = new List<string>();
        var fontIds = new Dictionary<string, int>(StringComparer.Ordinal) { ["Noto Sans"] = 0 }; var colorIds = new Dictionary<string, int>(StringComparer.Ordinal);
        var lists = new Dictionary<string, (int Id, ListItem Item)>(StringComparer.Ordinal);
        foreach (ParagraphBlock paragraph in model.Paragraphs)
        {
            context.CheckCancellation();
            if (paragraph.List != null && !lists.ContainsKey(paragraph.List.Id)) lists.Add(paragraph.List.Id, (lists.Count + 1, paragraph.List));
            if (lists.Count > 4096) throw new InvalidDataException("Limit.RtfDefinitions: RTF output lists exceed bounds.");
            foreach (TextRun run in paragraph.Runs)
            {
                string? name = run.Appearance.FontFamily;
                if (name != null && !fontIds.ContainsKey(name)) { fontIds.Add(name, fonts.Count); fonts.Add(name); }
                if (run.Color != null && !colorIds.ContainsKey(run.Color)) { colorIds.Add(run.Color, colors.Count); colors.Add(run.Color); }
                if (run.Appearance.Background != null && !colorIds.ContainsKey(run.Appearance.Background)) { colorIds.Add(run.Appearance.Background, colors.Count); colors.Add(run.Appearance.Background); }
                if (fonts.Count > 4096 || colors.Count > 4096 || lists.Count > 4096) throw new InvalidDataException("Limit.RtfDefinitions: RTF output definitions exceed bounds.");
            }
        }
        if (fonts.Count > 4096 || colors.Count > 4096 || lists.Count > 4096) throw new InvalidDataException("Limit.RtfDefinitions: RTF output definitions exceed bounds.");
        var text = new StringBuilder();
        text.Append(@"{\rtf1\ansi\ansicpg1252\deff0\uc1{\fonttbl");
        for (int i = 0; i < fonts.Count; i++) { text.Append(@"{\f").Append(i.ToString(CultureInfo.InvariantCulture)).Append(@"\fnil\fcharset0 "); Escape(fonts[i]); text.Append(";}"); }
        text.Append(@"}{\colortbl;");
        foreach (string color in colors)
            text.Append(@"\red").Append(Convert.ToByte(color.Substring(0, 2), 16).ToString(CultureInfo.InvariantCulture))
                .Append(@"\green").Append(Convert.ToByte(color.Substring(2, 2), 16).ToString(CultureInfo.InvariantCulture))
                .Append(@"\blue").Append(Convert.ToByte(color.Substring(4, 2), 16).ToString(CultureInfo.InvariantCulture)).Append(';');
        text.Append(@"}{\info{\title "); Escape(model.Title); text.Append(@"}{\author "); Escape(model.Author); text.Append("}}");
        if (lists.Count > 0)
        {
            text.Append(@"{\*\listtable");
            foreach (var list in lists.Values)
            {
                text.Append(@"{\list\listtemplateid").Append(list.Id.ToString(CultureInfo.InvariantCulture)).Append(@"{\listlevel\levelnfc")
                    .Append(list.Item.Ordered ? "0" : "23").Append(@"\leveljc0\levelfollow0\levelstartat").Append(list.Item.Start.ToString(CultureInfo.InvariantCulture));
                text.Append(list.Item.Ordered ? @"{\leveltext\'02\'00.;}{\levelnumbers\'01;}" : @"{\leveltext\'01\u8226?;}{\levelnumbers;}");
                text.Append(@"\fi-360\li720\jclisttab\tx720}\listid").Append(list.Id.ToString(CultureInfo.InvariantCulture)).Append('}');
            }
            text.Append(@"}{\*\listoverridetable");
            foreach (var list in lists.Values) text.Append(@"{\listoverride\listid").Append(list.Id.ToString(CultureInfo.InvariantCulture)).Append(@"\listoverridecount0\ls").Append(list.Id.ToString(CultureInfo.InvariantCulture)).Append('}');
            text.Append('}');
        }
        var counter = new ListCounter();
        foreach (DocumentBlock block in model.Blocks)
        {
            context.CheckCancellation();
            if (block is ParagraphBlock paragraph) Paragraph(paragraph, false);
            else
            {
                var table = (TableBlock)block;
                for (int r = 0; r < table.Rows.Count; r++)
                {
                    text.Append(@"\trowd\trgaph0\trleft0");
                    if (r < table.HeaderRowCount) text.Append(@"\trhdr");
                    if (table.NonSplittingRows.Contains(r)) text.Append(@"\trkeep");
                    double edge = 0;
                    foreach (TableCell cell in table.Rows[r])
                    {
                        if (cell.ColumnSpan != 1 || cell.Merge != VerticalMerge.None) context.Loss("Rtf.TableMerge", "RTF output flattens merged-cell structure into ordinary cells.");
                        edge += cell.CellWidth ?? (table.Width ?? 468) / table.Rows[r].Count;
                        text.Append(@"\cellx").Append(Twips(edge));
                    }
                    foreach (TableCell cell in table.Rows[r])
                    {
                        foreach (ParagraphBlock p in cell.Paragraphs) Paragraph(p, true);
                        text.Append(@"\cell ");
                    }
                    text.Append(@"\row ");
                }
            }
            Check();
        }
        text.Append('}'); Check(); byte[] bytes = Encoding.ASCII.GetBytes(text.ToString()); output.Write(bytes, 0, bytes.Length);

        void Paragraph(ParagraphBlock p, bool cell)
        {
            context.CheckCancellation(); text.Append(@"\pard\plain"); if (p.RightToLeft.HasValue) text.Append(p.RightToLeft.Value ? @"\rtlpar" : @"\ltrpar"); if (cell) text.Append(@"\intbl");
            text.Append(p.Alignment switch { ParagraphAlignment.Center => @"\qc", ParagraphAlignment.Right => @"\qr", ParagraphAlignment.Justified => @"\qj", _ => @"\ql" });
            text.Append(@"\li").Append(Twips(p.Layout.Left)).Append(@"\ri").Append(Twips(p.Layout.Right)).Append(@"\fi").Append(Twips(p.Layout.FirstLine));
            if (p.Layout.Before.HasValue) text.Append(@"\sb").Append(Twips(p.Layout.Before.Value));
            if (p.Layout.After.HasValue) text.Append(@"\sa").Append(Twips(p.Layout.After.Value));
            if (p.Layout.KeepTogether) text.Append(@"\keep"); if (p.Layout.KeepWithNext) text.Append(@"\keepn"); if (p.Layout.PageBreakBefore) text.Append(@"\pagebb");
            if (p.Layout.LineSpacing.HasValue || p.Layout.Tabs.Count > 0) context.Loss("Rtf.ParagraphLayout", "Custom line spacing and tab-stop definitions are outside this RTF writer profile.");
            if (p.HeadingLevel > 0) text.Append(@"\outlinelevel").Append((p.HeadingLevel - 1).ToString(CultureInfo.InvariantCulture));
            if (p.List != null)
            {
                if (p.List.Level != 0) context.Loss("Rtf.ListDepth", "RTF output flattens nested lists to a single editable level.");
                text.Append(@"\ls").Append(lists[p.List.Id].Id.ToString(CultureInfo.InvariantCulture)).Append(@"\ilvl0{\listtext "); Escape(counter.Label(p.List)); text.Append(@"\tab}");
            }
            text.Append(' ');
            foreach (TextRun run in p.Runs)
            {
                context.CheckCancellation();
                if (run.Image is DocumentImage image)
                {
                    if (image.AlternativeText.Length > 0) context.Loss("Rtf.ImageAlternativeText", "The RTF picture profile does not retain alternative text.");
                    text.Append(@"{\pict").Append(image.Extension == "png" ? @"\pngblip" : @"\jpegblip")
                        .Append(@"\picw").Append(image.PixelsWide.ToString(CultureInfo.InvariantCulture)).Append(@"\pich").Append(image.PixelsHigh.ToString(CultureInfo.InvariantCulture))
                        .Append(@"\picwgoal").Append(Twips(image.Width)).Append(@"\pichgoal").Append(Twips(image.Height)).Append(' ');
                    if ((long)image.Bytes.Length * 2 + text.Length > context.Options.MaximumOutputBytes) throw new InvalidDataException("Limit.OutputBytes: RTF picture exceeds output bounds.");
                    if (text.Length + (long)image.Bytes.Length * 2 > context.Options.MaximumOutputBytes) throw new InvalidDataException("Limit.OutputBytes: RTF picture expansion exceeds its output budget.");
                    foreach (byte value in image.Bytes) text.Append(value.ToString("x2", CultureInfo.InvariantCulture)); text.Append('}'); continue;
                }
                if (run.Link != null) context.Loss("Rtf.Link", "RTF hyperlinks retain visible text without executable field instructions.");
                if (run.Appearance.VerticalAlignment != null) context.Loss("Rtf.VerticalAlignment", "RTF vertical run alignment is not retained.");
                text.Append(@"{\f").Append((run.Appearance.FontFamily == null ? 0 : fontIds[run.Appearance.FontFamily]).ToString(CultureInfo.InvariantCulture))
                    .Append(@"\fs").Append(Math.Round((run.FontSize ?? 11) * 2).ToString(CultureInfo.InvariantCulture))
                    .Append(run.Bold ? @"\b" : @"\b0").Append(run.Italic ? @"\i" : @"\i0")
                    .Append(run.Appearance.Underline ? @"\ul" : @"\ulnone").Append(run.Appearance.Strike ? @"\strike" : @"\strike0")
                    .Append(@"\cf").Append((run.Color == null ? 0 : colorIds[run.Color] + 1).ToString(CultureInfo.InvariantCulture));
                if (run.Appearance.Background != null) text.Append(@"\highlight").Append((colorIds[run.Appearance.Background] + 1).ToString(CultureInfo.InvariantCulture));
                text.Append(' '); Escape(run.Text); text.Append('}'); Check();
            }
            text.Append(@"\par ");
        }
        void Escape(string value)
        {
            foreach (char c in value)
            {
                Check();
                if (c is '\\' or '{' or '}') text.Append('\\').Append(c);
                else if (c == '\n') text.Append(@"\line ");
                else if (c == '\t') text.Append(@"\tab ");
                else if (c == '\f') text.Append(@"\page ");
                else if (c >= 32 && c <= 126) text.Append(c);
                else text.Append(@"\u").Append(unchecked((short)c).ToString(CultureInfo.InvariantCulture)).Append('?');
            }
        }
        void Check() { if (text.Length > context.Options.MaximumOutputBytes) throw new InvalidDataException("Limit.OutputBytes: RTF output exceeds its byte bound."); }
        static string Twips(double points) => Math.Round(points * 20).ToString(CultureInfo.InvariantCulture);
    }
}
