"""Independent optional Arabic outline/text and PDF structure qualification."""
import argparse
import io
import ctypes
import ctypes.util
import re
import xml.etree.ElementTree as ET
from pathlib import Path
import fitz
import uharfbuzz as hb
from fontTools.ttLib import TTFont
from fontTools.pens.svgPathPen import SVGPathPen
from PIL import Image, ImageFilter

ARABIC = "مَرْحَبًا"
SVG = "http://www.w3.org/2000/svg"

def mask(data):
    with fitz.open(stream=data, filetype="svg") as doc:
        pix = doc[0].get_pixmap(matrix=fitz.Matrix(4, 4))
        image = Image.frombytes("RGB", (pix.width, pix.height), pix.samples).convert("L")
        return image.point(lambda x: 255 if x < 180 else 0)

def qualify(directory, fontpath):
    face = hb.Face(fontpath.read_bytes()); font = hb.Font(face); font.scale = (face.upem, face.upem)
    buffer = hb.Buffer(); buffer.add_str(ARABIC); buffer.guess_segment_properties(); hb.shape(font, buffer)
    fonttools = TTFont(fontpath); glyphset = fonttools.getGlyphSet(); order = fonttools.getGlyphOrder()
    reference = ET.Element("{" + SVG + "}svg", width="120pt", height="140pt", viewBox="0 0 120 140")
    penx = 0
    for info, position in zip(buffer.glyph_infos, buffer.glyph_positions):
        pen = SVGPathPen(glyphset); glyphset[order[info.codepoint]].draw(pen)
        ET.SubElement(reference, "{" + SVG + "}path", d=pen.getCommands(), transform=f"translate({10+(penx+position.x_offset)*.03} {100-position.y_offset*.03}) scale(.03 -.03)")
        penx += position.x_advance
    expected = mask(ET.tostring(reference)); expected_ink = list(expected.get_flattened_data())
    for direction in ("ltr", "rtl"):
        with fitz.open(directory / f"arabic-{direction}.pdf") as pdf:
            page = pdf[0]; assert page.get_text().replace("\n", "") == "Hello " + ARABIC + " 123 world."
            area, = page.search_for(ARABIC); assert abs(area.width - 63.12) < .03
            page.get_pixmap().save(directory / f"arabic-{direction}.png")
        root = ET.parse(directory / f"arabic-{direction}.svg").getroot()
        source = "Hello " + ARABIC + " 123 world."; size = len(source)
        fribidi = ctypes.CDLL(ctypes.util.find_library("fribidi")); log2vis = fribidi.fribidi_log2vis
        log2vis.argtypes = [ctypes.POINTER(ctypes.c_uint32), ctypes.c_int, ctypes.POINTER(ctypes.c_uint32), ctypes.POINTER(ctypes.c_uint32), ctypes.POINTER(ctypes.c_int), ctypes.POINTER(ctypes.c_int), ctypes.POINTER(ctypes.c_uint8)]
        logical = (ctypes.c_uint32 * size)(*map(ord, source)); visual = (ctypes.c_uint32 * size)(); base = ctypes.c_uint32(0x110 if direction == "ltr" else 0x111); mapping = (ctypes.c_int * size)(); reverse = (ctypes.c_int * size)(); levels = (ctypes.c_uint8 * size)()
        assert log2vis(logical, size, ctypes.byref(base), visual, mapping, reverse, levels) > 0
        groups = root.findall("{" + SVG + "}g"); position = 0; expected_order = []; actual_order = []
        for index, item in enumerate(groups):
            text = item.findtext("{" + SVG + "}title"); length = len(text)
            assert source[position:position+length] == text
            expected_order.append((min(mapping[position:position+length]), index)); position += length
            first_path = item.find("{" + SVG + "}path"); x = float(re.search(r"translate\(([-.0-9]+)", first_path.get("transform")).group(1)); actual_order.append((x,index))
        assert position == size
        assert [i for _,i in sorted(actual_order)] == [i for _,i in sorted(expected_order)], (direction, actual_order, expected_order)
        print(direction, "visual token order matches independent FriBidi")
        group, = [g for g in root.findall("{" + SVG + "}g") if g.findtext("{" + SVG + "}title") == ARABIC]
        first = group.find("{" + SVG + "}path"); m = re.search(r"translate\(([-.0-9]+) ([-.0-9]+)\)", first.get("transform"))
        x, y = map(float, m.groups())
        # First HB glyph has zero offsets; move production output to the independent reference frame.
        group.set("transform", f"translate({10-x} {100-y})")
        actualroot = ET.Element("{" + SVG + "}svg", width="120pt", height="140pt", viewBox="0 0 120 140"); actualroot.append(group)
        actual = list(mask(ET.tostring(actualroot)).get_flattened_data())
        intersection = sum(a > 0 and b > 0 for a,b in zip(actual, expected_ink)); union = sum(a > 0 or b > 0 for a,b in zip(actual, expected_ink))
        assert intersection / union > .98, intersection / union
        observed = mask(ET.tostring(actualroot)); observed.save(directory / "arabic-observed-mask.png"); expected.save(directory / "arabic-reference-mask.png"); assert all(abs(a-b) <= 1 for a,b in zip(observed.getbbox(), expected.getbbox()))
        a_dilated = list(observed.filter(ImageFilter.MaxFilter(3)).get_flattened_data()); b_dilated = list(expected.filter(ImageFilter.MaxFilter(3)).get_flattened_data())
        assert all(a == 0 or b > 0 for a,b in zip(actual, b_dilated))
        assert all(b == 0 or a > 0 for a,b in zip(a_dilated, expected_ink))
        print(direction, "Arabic logical text, search bounds and independent outline mask PASS", round(intersection / union, 6))
    for filename in ("accessible-ua1.pdf", "accessible-multipage.pdf"):
        with fitz.open(directory / filename) as pdf:
            catalog = pdf.pdf_catalog(); assert pdf.xref_get_key(catalog, "Lang")[1].startswith("en")
            struct = int(pdf.xref_get_key(catalog, "StructTreeRoot")[1].split()[0]); parent = int(pdf.xref_get_key(struct, "ParentTree")[1].split()[0]); assert pdf.xref_get_key(parent, "Nums")[0] == "array"
            roles = []; marks = {}; text = []; decoded = {}
            for page in pdf:
                maps = {}
                for font in page.get_fonts():
                    program = pdf.extract_font(font[0])[3]; assert program
                    assert TTFont(io.BytesIO(program))["maxp"].numGlyphs > 0
                    to_unicode = int(pdf.xref_get_key(font[0], "ToUnicode")[1].split()[0]); cmap = pdf.xref_stream(to_unicode).decode("ascii")
                    maps[font[4]] = {int(a,16): bytes.fromhex(b).decode("utf-16-be") for a,b in re.findall(r"<([0-9A-Fa-f]{2})><([0-9A-Fa-f]{4,})>", cmap)}
                content = b"".join(pdf.xref_stream(n) for n in page.get_contents()).decode("latin1")
                for mcid, body in re.findall(r"/MCID (\d+) >> BDC(.*?)EMC", content, re.S):
                    name = re.search(r"/(F\d+) [\d.]+ Tf", body); segments = re.findall(r"<([0-9A-Fa-f]+)> Tj", body)
                    if name: decoded[str(page.xref),mcid] = "".join(maps[name.group(1)][byte] for segment in segments for byte in bytes.fromhex(segment))
            def walk(number):
                role = pdf.xref_get_key(number, "S")[1]; roles.append(role)
                kind, kids = pdf.xref_get_key(number, "K")
                for ref in re.finditer(r"(?<!/Pg )(?<!/Obj )(?<!\d)(\d+) 0 R", kids):
                    child = int(ref.group(1))
                    if pdf.xref_get_key(child, "Type")[1] == "/StructElem": walk(child)
                for page, mcid in re.findall(r"/Pg (\d+) 0 R/MCID (\d+)", kids):
                    assert (page,mcid) not in marks; marks[page,mcid] = number; text.append(decoded.get((page,mcid), ""))
            walk(int(pdf.xref_get_key(struct, "K")[1].split()[0]))
            assert roles[0] == "/Document" and "/H1" in roles and "/TH" in roles and "/TD" in roles
            for page in pdf:
                key = pdf.xref_get_key(page.xref, "StructParents"); assert key[0] == "int"
                content = b"".join(pdf.xref_stream(n) for n in page.get_contents())
                ids = re.findall(rb"/MCID (\d+)", content); assert len(ids) == len(set(ids))
                for mcid in ids: assert (str(page.xref),mcid.decode()) in marks
            if "multipage" not in filename:
                assert "".join(text) == "InvoiceSummary first.ProductPriceService42.50Terms"
            if "multipage" in filename:
                assert all(r in roles for r in ("/L", "/LI", "/Lbl", "/LBody", "/Figure"))
                assert roles.count("/Figure") == 1  # second source image was explicitly decorative
                assert "".join(text).startswith("Accessibility sample1. First action2. Second actionFinal instruction.Column AColumn BRecord 1Value 1")
                assert "".join(text).endswith("Record 49Value 49")
                assert roles.count("/TH") == 2  # repeated visual header copies remain artifacts
            print(filename, "structure, MCIDs, roles, language and page references PASS")
    with fitz.open(directory / "accessible-early-link.pdf") as pdf:
        assert len(pdf) == 2
        structure = int(pdf.xref_get_key(pdf.pdf_catalog(), "StructTreeRoot")[1].split()[0])
        parent_tree = int(pdf.xref_get_key(structure, "ParentTree")[1].split()[0])
        entries = re.findall(r"(\d+)\s*(\[[^\]]*\]|\d+\s+0\s+R)", pdf.xref_get_key(parent_tree, "Nums")[1])
        keys = [int(key) for key, value in entries]
        assert keys == sorted(set(keys)), keys
        parents = dict((int(key), value) for key, value in entries)
        for page in pdf:
            key = int(pdf.xref_get_key(page.xref, "StructParents")[1])
            assert parents[key].startswith("[")
            for link in page.get_links():
                annotation = link["xref"]
                key = int(pdf.xref_get_key(annotation, "StructParent")[1])
                owner = int(parents[key].split()[0])
                assert pdf.xref_get_key(owner, "S")[1] == "/Link"
                kids = pdf.xref_get_key(owner, "K")[1]
                assert re.search(r"/Obj\s+" + str(annotation) + r"\s+0\s+R", kids)
                assert re.search(r"/Pg\s+" + str(page.xref) + r"\s+0\s+R", kids)
        print("Early-page link ParentTree keys, page arrays and annotation OBJR references PASS")
    print("Independent tools:", "PyMuPDF", fitz.VersionBind, "uharfbuzz", hb.__version__, "HarfBuzz", hb.version_string())

if __name__ == "__main__":
    p = argparse.ArgumentParser(description=__doc__); p.add_argument("artifacts", type=Path); args = p.parse_args()
    qualify(args.artifacts, Path(__file__).resolve().parents[1] / "tests/wordnet.Conversion.Tests/Fixtures/Interoperability/NotoSansArabic-Regular.ttf")
