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
    private sealed class FontInfo
    {
        internal StringBuilder Name = new StringBuilder();
        internal int? CodePage;
    }
    private sealed class ListInfo { internal int Id, Levels; internal bool Ordered = true; internal int Start = 1; }
    private sealed class Picture : IDisposable
    {
        internal string Kind = "";
        internal int? Width, Height;
        internal StringBuilder Hex = new StringBuilder();
        internal MemoryStream Binary = new MemoryStream();
        public void Dispose() => Binary.Dispose();
    }
    private sealed class State
    {
        internal bool Skip, Star, Bold, Italic, Underline, Strike, InTable;
        internal string Destination = "body";
        internal int Uc = 1, Font, Color, Background, Heading, Red, Green, Blue, List, Level, OverrideId, DefinitionLevel;
        internal bool ColorSeen;
        internal double Size = 11;
        internal ParagraphAlignment Alignment;
        internal ParagraphLayout Layout = new ParagraphLayout();
        internal FontInfo? FontEntry;
        internal ListInfo? ListEntry;
        internal Picture? Picture;
        internal State Copy() { var copy = (State)MemberwiseClone(); copy.Layout = Layout.Copy(); return copy; }
    }

    internal static DocumentModel Read(byte[] input, ConversionContext context) { using var reader = new Reader(input, context); return reader.Read(); }

    private sealed class Reader : IDisposable
    {
        private readonly byte[] input;
        private readonly ConversionContext context;
        private readonly DocumentModel model = new DocumentModel();
        private readonly Stack<State> groups = new Stack<State>();
        private readonly Dictionary<int, FontInfo> fonts = new Dictionary<int, FontInfo>();
        private readonly List<string?> colors = new List<string?>();
        private readonly Dictionary<int, ListInfo> lists = new Dictionary<int, ListInfo>();
        private readonly Dictionary<int, int> overrides = new Dictionary<int, int>();
        private readonly Dictionary<int, Encoding> encodings = new Dictionary<int, Encoding>();
        private readonly List<Picture> pictures = new List<Picture>();
        private readonly StringBuilder text = new StringBuilder(), title = new StringBuilder(), author = new StringBuilder();
        private State state = new State();
        private ParagraphBlock paragraph = new ParagraphBlock();
        private List<ParagraphBlock> cellParagraphs = new List<ParagraphBlock>();
        private List<TableCell>? row;
        private readonly List<double> edges = new List<double>();
        private TableBlock? table;
        private int offset, characters, fallback, codePage = 1252;
        private bool rootClosed, header, rowHeader, rowKeep;

        internal Reader(byte[] input, ConversionContext context) { this.input = input; this.context = context; }
        public void Dispose() { foreach (Picture picture in pictures) picture.Dispose(); }

        internal DocumentModel Read()
        {
            if (input.Length < 6 || input[0] != '{' || input[1] != '\\' || input[2] != 'r' || input[3] != 't' || input[4] != 'f') throw Invalid("Missing RTF header.");
            while (offset < input.Length)
            {
                context.CheckCancellation(); byte b = input[offset++];
                if (rootClosed) { if (b is not 10 and not 13 and not 32) throw Invalid("Data after the root group."); continue; }
                if (b == '{')
                {
                    Flush(); fallback = 0;
                    if (groups.Count >= context.Options.MaximumNestingDepth) throw new InvalidDataException("Limit.RtfDepth: RTF group nesting exceeds the limit.");
                    groups.Push(state); state = state.Copy(); state.Star = false;
                }
                else if (b == '}')
                {
                    Flush(); fallback = 0;
                    if (groups.Count == 0) throw Invalid("Unbalanced closing group.");
                    Picture? picture = state.Picture; State previous = groups.Pop();
                    if (picture != null && !ReferenceEquals(picture, previous.Picture)) FinishPicture(picture);
                    if (groups.Count == 0 && paragraph.Runs.Count > 0) FinishParagraph(false);
                    state = previous; if (groups.Count == 0) rootClosed = true;
                }
                else if (b == '\\') Control();
                else if (b is not 10 and not 13) Literal(b);
            }
            if (!header || !rootClosed || groups.Count > 0) throw Invalid("Unclosed RTF group or invalid version.");
            Flush(); if (row != null) throw Invalid("Unclosed table row.");
            if (paragraph.Runs.Count > 0) FinishParagraph(false);
            model.Title = title.ToString(); model.Author = author.ToString();
            foreach (TextRun run in model.Paragraphs.SelectMany(p => p.Runs))
                try { TextFormat.Utf8.GetByteCount(run.Text); }
                catch (EncoderFallbackException e) { throw new InvalidDataException("Rtf.Unicode: Unpaired Unicode surrogate.", e); }
            return model;
        }

        private void Literal(byte value)
        {
            if (fallback > 0) { fallback--; return; }
            if (state.Skip) return;
            if (state.Destination == "pict")
            {
                if (value is 9 or 32) return;
                if (!Uri.IsHexDigit((char)value)) throw Invalid("Invalid picture hex data.");
                Picture picture = state.Picture!;
                if (picture.Hex.Length >= (long)context.Options.MaximumExpandedBytes * 2) throw new InvalidDataException("Limit.RtfPicture: Picture data exceeds expansion bounds.");
                picture.Hex.Append((char)value); return;
            }
            if (value < 32) throw Invalid("Unescaped control byte.");
            if (state.Destination == "colors" && value == ';')
            {
                if (colors.Count >= 4096) throw new InvalidDataException("Limit.RtfColors: Too many colors.");
                colors.Add(state.ColorSeen ? state.Red.ToString("X2", CultureInfo.InvariantCulture) + state.Green.ToString("X2", CultureInfo.InvariantCulture) + state.Blue.ToString("X2", CultureInfo.InvariantCulture) : null);
                state.Red = state.Green = state.Blue = 0; state.ColorSeen = false; return;
            }
            if (value < 128) { Append(((char)value).ToString()); return; }
            int cp = state.Destination != "font" && fonts.TryGetValue(state.Font, out FontInfo? face) ? face.CodePage ?? codePage : codePage;
            if (!encodings.TryGetValue(cp, out Encoding? encoding))
            {
                encoding = CodePagesEncodingProvider.Instance.GetEncoding(cp, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
                if (encoding == null || !encoding.IsSingleByte) throw new NotSupportedException("Rtf.CodePage: Only supported single-byte ANSI code pages are imported; use Unicode escapes for other scripts.");
                encodings.Add(cp, encoding);
            }
            try { Append(encoding.GetString(new[] { value })); }
            catch (DecoderFallbackException e) { throw new InvalidDataException("Rtf.CodePage: Invalid encoded byte.", e); }
        }

        private void Append(string value)
        {
            if (state.Skip) return;
            characters = checked(characters + value.Length); context.CheckCharacters(characters);
            if (state.Destination == "font") { if (state.FontEntry != null && value != ";") state.FontEntry.Name.Append(value); return; }
            if (state.Destination == "title") { title.Append(value); return; }
            if (state.Destination == "author") { author.Append(value); return; }
            if (state.Destination == "body") text.Append(value);
        }

        private void Control()
        {
            if (offset >= input.Length) throw Invalid("Incomplete control.");
            char first = (char)input[offset++];
            if (!AsciiLetter((byte)first))
            {
                if (first == '*') { state.Star = true; return; }
                if (first == '\'')
                {
                    if (offset + 2 > input.Length) throw Invalid("Incomplete hex escape.");
                    int a = Hex(input[offset++]), b = Hex(input[offset++]); Literal((byte)(a * 16 + b)); return;
                }
                if (fallback > 0) { fallback--; return; }
                if (first is '\\' or '{' or '}') Append(first.ToString());
                else if (first == '~') Append("\u00a0");
                else if (first == '_') Append("\u2011");
                else if (first == '-') Append("\u00ad");
                else if (first is not '\n' and not '\r') context.Loss("Rtf.Control", "An unsupported RTF control symbol is omitted.");
                return;
            }
            int start = offset - 1;
            while (offset < input.Length && AsciiLetter(input[offset])) offset++;
            if (offset - start > 64) throw Invalid("Control word is too long.");
            string word = Encoding.ASCII.GetString(input, start, offset - start);
            bool negative = offset < input.Length && input[offset] == '-'; if (negative) offset++;
            int digits = offset; long number = 0;
            while (offset < input.Length && input[offset] >= '0' && input[offset] <= '9')
            { number = number * 10 + input[offset++] - '0'; if (number > int.MaxValue) throw Invalid("Control argument overflows."); }
            if (negative && digits == offset) throw Invalid("Missing signed control argument.");
            int? argument = digits == offset ? null : (int)(negative ? -number : number);
            if (offset < input.Length && input[offset] == 32) offset++;
            if (word == "bin")
            {
                int length = argument ?? -1;
                if (length < 0 || length > input.Length - offset || length > context.Options.MaximumExpandedBytes) throw Invalid("Invalid binary payload length.");
                if (fallback > 0) fallback--;
                else if (!state.Skip && state.Picture != null)
                {
                    if (state.Picture.Binary.Length + length > context.Options.MaximumExpandedBytes) throw new InvalidDataException("Limit.RtfPicture: Binary picture exceeds expansion bounds.");
                    state.Picture.Binary.Write(input, offset, length);
                }
                else if (!state.Skip) context.Loss("Rtf.Binary", "Binary content outside a supported picture destination is omitted.");
                offset += length; return;
            }
            if (fallback > 0) { fallback--; return; }
            if (word == "u")
            {
                int value = argument ?? int.MaxValue; if (value < short.MinValue || value > ushort.MaxValue) throw Invalid("Invalid Unicode escape.");
                Append(((char)(ushort)value).ToString()); fallback = state.Uc; return;
            }
            if (word == "uc") { int count = argument ?? 1; if (count < 0 || count > 16) throw Invalid("Unicode fallback count is outside the supported bound."); state.Uc = count; return; }
            Flush();
            if (state.Star)
            {
                state.Star = false;
                if (word is not "listtable" and not "listoverridetable") { state.Skip = true; context.Loss("Rtf.Destination", "An unsupported ignorable destination is omitted without execution."); }
            }
            if (state.Skip) return;
            int n = argument ?? 1;
            switch (word)
            {
                case "rtf": if (n != 1) throw Invalid("Unsupported RTF version."); header = true; break;
                case "ansicpg": codePage = n; break;
                case "uc": if (n < 0 || n > 16) throw Invalid("Unicode fallback count is outside the supported bound."); state.Uc = n; break;
                case "fonttbl": state.Destination = "font"; break;
                case "colortbl": state.Destination = "colors"; break;
                case "info": state.Destination = "info"; break;
                case "title": state.Destination = "title"; break;
                case "author": state.Destination = "author"; break;
                case "deff": state.Font = n; break;
                case "f":
                    state.Font = n;
                    if (state.Destination == "font")
                    { if (fonts.Count >= 4096) throw new InvalidDataException("Limit.RtfFonts: Too many font records."); fonts[n] = state.FontEntry = new FontInfo(); }
                    break;
                case "fcharset":
                    if (state.FontEntry != null) state.FontEntry.CodePage = n switch { 0 or 1 => null, 204 => 1251, 238 => 1250, 161 => 1253, 162 => 1254, 177 => 1255, 178 => 1256, 186 => 1257, 163 => 1258, 128 => 932, 129 => 949, 134 => 936, 136 => 950, _ => -1 };
                    break;
                case "red":
                case "green":
                case "blue":
                    if (n < 0 || n > 255) throw Invalid("Invalid RGB component.");
                    if (word == "red") state.Red = n; else if (word == "green") state.Green = n; else state.Blue = n; state.ColorSeen = true; break;
                case "cf": state.Color = n; break;
                case "highlight": state.Background = n; break;
                case "outlinelevel": if (n < 0 || n > 9) throw Invalid("Invalid outline level."); state.Heading = n == 9 ? 0 : n + 1; break;
                case "b": state.Bold = n != 0; break;
                case "i": state.Italic = n != 0; break;
                case "ul": state.Underline = n != 0; break;
                case "ulnone": state.Underline = false; break;
                case "strike": state.Strike = n != 0; break;
                case "fs": if (n <= 0 || n > 3276) throw Invalid("Invalid font size."); state.Size = n / 2.0; break;
                case "plain": state.Bold = state.Italic = state.Underline = state.Strike = false; state.Size = 11; state.Color = state.Background = 0; break;
                case "pard": state.Alignment = ParagraphAlignment.Left; state.Layout = new ParagraphLayout(); state.List = state.Level = state.Heading = 0; state.InTable = false; break;
                case "ql": state.Alignment = ParagraphAlignment.Left; break;
                case "qc": state.Alignment = ParagraphAlignment.Center; break;
                case "qr": state.Alignment = ParagraphAlignment.Right; break;
                case "qj": state.Alignment = ParagraphAlignment.Justified; break;
                case "li": state.Layout.Left = Twips(n); break;
                case "ri": state.Layout.Right = Twips(n); break;
                case "fi": state.Layout.FirstLine = Twips(n); break;
                case "sb": state.Layout.Before = Twips(n); break;
                case "sa": state.Layout.After = Twips(n); break;
                case "keep": state.Layout.KeepTogether = n != 0; break;
                case "keepn": state.Layout.KeepWithNext = n != 0; break;
                case "pagebb": state.Layout.PageBreakBefore = n != 0; break;
                case "par": FinishParagraph(true); break;
                case "line": Append("\n"); break;
                case "tab": Append("\t"); break;
                case "page": Append("\f"); break;
                case "emdash": Append("\u2014"); break;
                case "endash": Append("\u2013"); break;
                case "lquote": Append("\u2018"); break;
                case "rquote": Append("\u2019"); break;
                case "ldblquote": Append("\u201c"); break;
                case "rdblquote": Append("\u201d"); break;
                case "listtable": case "listoverridetable": state.Destination = word; break;
                case "list": state.ListEntry = new ListInfo(); break;
                case "listlevel": if (state.ListEntry != null) state.DefinitionLevel = state.ListEntry.Levels++; break;
                case "listid":
                    if (state.Destination == "listtable" && state.ListEntry != null) { state.ListEntry.Id = n; lists[n] = state.ListEntry; }
                    else state.OverrideId = n;
                    break;
                case "levelnfc":
                case "levelnfcn":
                    if (state.ListEntry != null && state.DefinitionLevel == 0) { if (n is not 0 and not 23) context.Loss("Rtf.ListFormat", "Unsupported list formats become decimal numbering."); state.ListEntry.Ordered = n != 23; }
                    break;
                case "levelstartat": if (state.ListEntry != null && state.DefinitionLevel == 0) { if (n < 0) throw Invalid("Invalid list start."); state.ListEntry.Start = n; } break;
                case "ls": if (state.Destination == "listoverridetable") overrides[n] = state.OverrideId; else state.List = n; break;
                case "ilvl": if (n < 0 || n > 8) throw Invalid("List depth exceeds nine levels."); state.Level = n; break;
                case "leveltext": case "levelnumbers": case "listtext": case "pntext": state.Skip = true; break;
                case "trowd":
                    if (row != null) throw Invalid("Nested table rows are outside the RTF profile."); row = new List<TableCell>(); edges.Clear(); state.InTable = true; rowHeader = rowKeep = false; break;
                case "cellx":
                    double edge = Twips(n); if (edge <= (edges.Count > 0 ? edges[edges.Count - 1] : 0) || edges.Count >= 1024) throw Invalid("Invalid table cell edge."); edges.Add(edge); break;
                case "trhdr": rowHeader = n != 0; break;
                case "trkeep": rowKeep = n != 0; break;
                case "cpg": if (state.FontEntry != null) state.FontEntry.CodePage = n; break;
                case "intbl": state.InTable = n != 0; break;
                case "cell": FinishCell(); break;
                case "row": FinishRow(); break;
                case "pict":
                    if (state.Picture != null) throw Invalid("Nested picture destination.");
                    if (pictures.Count >= context.Options.MaximumArchiveEntries) throw new InvalidDataException("Limit.RtfPictures: Too many picture destinations.");
                    if (state.Destination != "body") { state.Skip = true; context.Loss("Rtf.PicturePlacement", "Pictures outside body content are omitted."); break; }
                    state.Destination = "pict"; state.Picture = new Picture(); pictures.Add(state.Picture); break;
                case "pngblip": case "jpegblip": if (state.Picture != null) state.Picture.Kind = word; break;
                case "picwgoal": if (state.Picture != null) state.Picture.Width = n; break;
                case "pichgoal": if (state.Picture != null) state.Picture.Height = n; break;
                case "field": context.Loss("Rtf.Field", "Fields retain cached result text without executable instructions."); break;
                case "fldrslt": state.Destination = "body"; break;
                case "fldinst":
                case "object":
                case "header":
                case "footer":
                case "footnote":
                case "stylesheet":
                case "generator":
                case "filetbl":
                case "datastore":
                case "xmlnstbl":
                case "shp":
                    state.Skip = true; context.Loss("Rtf.Destination", "An unsupported RTF destination is omitted without execution or fetching."); break;
                case "ansi": case "viewkind": case "viewscale": case "deftab": case "deflang": case "deflangfe": case "lang": case "langfe": case "nouicompat": case "fnil": case "froman": case "fswiss": case "fmodern": case "fprq": case "falt": case "ftech": case "fbidi": case "fdecor": case "loch": case "hich": case "dbch": case "ltrch": case "ltrpar": case "af": case "afs": case "adeflang": case "listtemplateid": case "listoverride": case "listoverridecount": case "leveljc": case "levelfollow": case "levelspace": case "levelindent": case "jclisttab": case "tx": case "picw": case "pich": case "picscalex": case "picscaley": case "trgaph": case "trleft": break;
                default: context.Loss("Rtf.Control", "Formatting outside the documented RTF control-word profile is not retained."); break;
            }
            if (fonts.Count > 4096 || lists.Count > 4096 || overrides.Count > 4096) throw new InvalidDataException("Limit.RtfDefinitions: Too many RTF definitions.");
        }

        private void Flush()
        {
            if (text.Length == 0) return;
            var appearance = new RunAppearance { Underline = state.Underline, Strike = state.Strike };
            if (state.Background < 0 || state.Background >= colors.Count && state.Background != 0) throw Invalid("Invalid highlight reference.");
            appearance.Background = state.Background == 0 ? null : colors[state.Background];
            if (fonts.TryGetValue(state.Font, out FontInfo? face))
            {
                appearance.FontFamily = face.Name.ToString().Trim();
                if (face.CodePage == -1) context.Loss("Rtf.FontCharset", "A special font charset retains literal character values without a symbol-font encoding map.");
            }
            if (state.Color < 0 || state.Color >= colors.Count && state.Color != 0) throw Invalid("Invalid color table reference.");
            paragraph.HeadingLevel = state.Heading; paragraph.Alignment = state.Alignment; paragraph.Layout = state.Layout.Copy();
            if (state.List != 0)
            {
                if (state.Level != 0) context.Loss("Rtf.ListDepth", "RTF input uses the first list-level definition for deeper items in this bounded profile.");
                int id = overrides.TryGetValue(state.List, out int original) ? original : state.List;
                if (!lists.TryGetValue(id, out ListInfo? info)) { context.Loss("Rtf.ListDefinition", "An undefined list uses decimal numbering."); info = new ListInfo(); }
                paragraph.List = new ListItem { Id = "rtf-" + id.ToString(CultureInfo.InvariantCulture), Level = state.Level, Ordered = info.Ordered, Start = info.Start };
            }
            paragraph.Runs.Add(new TextRun(text.ToString(), state.Bold, state.Italic, fontSize: state.Size, color: state.Color == 0 || colors.Count == 0 ? null : colors[state.Color], appearance: appearance)); text.Clear();
        }
        private void FinishParagraph(bool force)
        {
            Flush(); if (!force && paragraph.Runs.Count == 0) return;
            paragraph.HeadingLevel = state.Heading; paragraph.Alignment = state.Alignment; paragraph.Layout = state.Layout.Copy();
            if (state.InTable || row != null) cellParagraphs.Add(paragraph);
            else { model.Blocks.Add(paragraph); table = null; }
            paragraph = new ParagraphBlock();
        }
        private void FinishCell()
        {
            if (row == null) throw Invalid("Cell outside a table row.");
            FinishParagraph(cellParagraphs.Count == 0 || paragraph.Runs.Count > 0);
            var cell = new TableCell(); cell.Paragraphs.AddRange(cellParagraphs); cellParagraphs.Clear(); row.Add(cell);
            if (row.Count > 1024) throw new InvalidDataException("Limit.RtfCells: Too many row cells.");
        }
        private void FinishRow()
        {
            if (row == null || row.Count == 0 || row.Count != edges.Count || cellParagraphs.Count > 0 || paragraph.Runs.Count > 0) throw Invalid("Unclosed cells or inconsistent table grid.");
            if (table == null) { table = new TableBlock(); model.Blocks.Add(table); }
            double previous = 0;
            for (int i = 0; i < row.Count; i++) { row[i].CellWidth = edges[i] - previous; previous = edges[i]; }
            if (table.Rows.Count == 0) { table.ColumnWidths.AddRange(row.Select(c => c.CellWidth!.Value)); table.Width = previous; }
            if (rowHeader && table.HeaderRowCount == table.Rows.Count) table.HeaderRowCount++;
            if (rowKeep) table.NonSplittingRows.Add(table.Rows.Count);
            table.Rows.Add(row); row = null; state.InTable = false;
            if (table.Rows.Count > 100000) throw new InvalidDataException("Limit.RtfRows: Too many table rows.");
        }
        private void FinishPicture(Picture picture)
        {
            try
            {
                if (picture.Kind is not "pngblip" and not "jpegblip") { context.Loss("Rtf.Picture", "Only embedded PNG/JPEG pictures are retained."); return; }
                if (picture.Binary.Length > 0 && picture.Hex.Length > 0 || picture.Hex.Length % 2 != 0) throw Invalid("Invalid mixed or odd-length picture payload.");
                byte[] bytes = picture.Binary.Length > 0 ? picture.Binary.ToArray() : new byte[picture.Hex.Length / 2];
                if (picture.Binary.Length == 0) for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)(Hex((byte)picture.Hex[i * 2]) * 16 + Hex((byte)picture.Hex[i * 2 + 1]));
                DocumentImage image = DocumentImage.Read(bytes, picture.Width.HasValue ? Twips(picture.Width.Value) : null, picture.Height.HasValue ? Twips(picture.Height.Value) : null, "", context);
                paragraph.Runs.Add(new TextRun("", image: image));
            }
            finally { picture.Binary.Dispose(); }
        }
        private static double Twips(int n) { if (n < -31680 || n > 31680) throw Invalid("Twip dimension exceeds the 22-inch profile."); return n / 20.0; }
        private static int Hex(byte b) => b >= '0' && b <= '9' ? b - '0' : b >= 'a' && b <= 'f' ? b - 'a' + 10 : b >= 'A' && b <= 'F' ? b - 'A' + 10 : throw Invalid("Invalid hexadecimal escape.");
        private static InvalidDataException Invalid(string reason) => new InvalidDataException("Rtf.Structure: " + reason);
        private static bool AsciiLetter(byte b) => b >= 'a' && b <= 'z' || b >= 'A' && b <= 'Z';
    }
}
