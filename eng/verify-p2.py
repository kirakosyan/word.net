#!/usr/bin/env python3
"""Independent content/geometry/reading checks for the bounded P2 corpus.

Run after object/native tests with WORDNET_P2_EVIDENCE set and the LibreOffice
UNO edit driver. No converter implementation is imported by this verifier.
"""
import argparse
import hashlib
import io
import json
import re
import xml.etree.ElementTree as ET
from pathlib import Path
from zipfile import ZipFile
import fitz
import uharfbuzz as hb
from fontTools.ttLib import TTFont

ROOT = Path(__file__).resolve().parents[1]
NS = {'c': 'http://schemas.openxmlformats.org/drawingml/2006/chart',
      'm': 'http://schemas.openxmlformats.org/officeDocument/2006/math',
      's': 'http://schemas.openxmlformats.org/spreadsheetml/2006/main',
      'svg': 'http://www.w3.org/2000/svg'}
TOLERANCE = 0.5  # Locked in docs/p2-objects-typography.md before acceptance.


def verify(directory, office):
    for filename, reference in [('STIXTwoMath.ttf', 'math-reference.json'), ('NotoSansDevanagari-Regular.ttf', 'devanagari-reference.json')]:
        folder = ROOT / 'tests/wordnet.Conversion.Tests/Fixtures/P2'
        assert hashlib.sha256((folder / filename).read_bytes()).hexdigest() == json.loads((folder / reference).read_text())['sha256']
    fontbytes = (ROOT / 'src/wordnet/Resources/Fonts/NotoSans-Regular.ttf').read_bytes()
    face = hb.Face(fontbytes); font = hb.Font(face); font.scale = (face.upem, face.upem)
    hb.ot_font_set_funcs(font)
    def width(text, size=8):
        b = hb.Buffer(); b.add_str(text); b.guess_segment_properties(); hb.shape(font, b)
        return sum(p.x_advance for p in b.glyph_positions) * size / face.upem
    # Authored Values chart contract: 300 x 180 pt at the default 48 pt margin;
    # range [-5,10], 5-unit ticks; zero is a point and null is a gap.
    left = 8 + max(width(v) for v in ('-5', '0', '5', '10')) + 6
    right = 300 - 8 - max(width(v) for v in ('North', 'South')) - 26
    slot = (right - left) / 4; bar = slot * .8 / 2; zero = 48 + 180 - 24 - 5 / 15 * 130
    with fitz.open(directory / 'objects.pdf') as pdf:
        assert len(pdf) == 1
        bars = [d['rect'] for d in pdf[0].get_drawings() if d['type'] == 'f' and d['fill'] and
                all(abs(a-b) < .001 for a, b in zip(d['fill'], (36/255, 104/255, 172/255))) and d['rect'].width > 10]
        assert len(bars) == 4
        for i, (rect, value) in enumerate(zip(bars, (10, 0, -2, 6))):
            y = zero - value / 15 * 130
            expected = (48 + left + i * slot + slot * .1, min(y, zero),
                        48 + left + i * slot + slot * .1 + bar, max(y, zero))
            assert max(abs(a-b) for a, b in zip(rect, expected)) <= TOLERANCE, (rect, expected)
        assert 'x squared divided by square root of three' in ''.join(p.get_text() for p in pdf)
    # Independent font-tool constants and glyphs predict x/2 baselines and bar thickness.
    mathfont = TTFont(ROOT / 'tests/wordnet.Conversion.Tests/Fixtures/P2/STIXTwoMath.ttf')
    constants = mathfont['MATH'].table.MathConstants; units = mathfont['head'].unitsPerEm
    root = ET.parse(directory / 'math-simple.svg').getroot()
    groups = [g for g in root.findall('svg:g', NS) if g.find('svg:path', NS) is not None]
    assert len(groups) == 2
    positions = [tuple(map(float, re.search(r'translate\(([-.0-9]+) ([-.0-9]+)\)', g.find('svg:path', NS).get('transform')).groups())) for g in groups]
    expected_gap = (constants.FractionNumeratorDisplayStyleShiftUp.Value + constants.FractionDenominatorDisplayStyleShiftDown.Value) * 12 / units
    assert abs(positions[1][1] - positions[0][1] - expected_gap) <= TOLERANCE
    rule = constants.FractionRuleThickness.Value * 12 / units
    assert any(abs(float(r.get('height', 0)) - rule) <= .000001 for r in root.findall('svg:rect', NS) if '%' not in r.get('height', ''))
    # Native logical carriers are inspected in content order, independently of outlines.
    for name, language in [('native-hindi', 'hi-IN'), ('native-arabic', 'ar')]:
        expected = (directory / (name + '.txt')).read_text()
        with fitz.open(directory / (name + '.pdf')) as pdf:
            assert pdf.xref_get_key(pdf.pdf_catalog(), 'Lang')[1] == language
            text = ''.join(p.get_text() for p in pdf).replace('\n', '')
            assert text == expected.replace('\n', ''), (name, text, expected)
            structure = int(pdf.xref_get_key(pdf.pdf_catalog(), 'StructTreeRoot')[1].split()[0])
            parent = int(pdf.xref_get_key(structure, 'ParentTree')[1].split()[0])
            assert pdf.xref_get_key(parent, 'Nums')[0] == 'array'
            all_actual = []; marks = set()
            for page in pdf:
                stream = b''.join(pdf.xref_stream(n) for n in page.get_contents()).decode('latin1')
                actual = re.findall(r'/ActualText\s*<([0-9a-fA-F]+)>', stream)
                all_actual.extend(bytes.fromhex(h).decode('utf-16') for h in actual)
                ids = re.findall(r'/MCID (\d+)', stream)
                assert len(ids) == len(set(ids))
                marks.update((page.xref, int(m)) for m in ids)
            assert ''.join(all_actual) == expected.replace('\n', '')
            assert len(marks) == len(all_actual)
    with fitz.open(directory / 'native-hindi-link.pdf') as pdf:
        structure = int(pdf.xref_get_key(pdf.pdf_catalog(), 'StructTreeRoot')[1].split()[0])
        parent = int(pdf.xref_get_key(structure, 'ParentTree')[1].split()[0])
        entries = re.findall(r'(\d+)\s*(\[[^\]]*\]|\d+\s+0\s+R)', pdf.xref_get_key(parent, 'Nums')[1])
        keys = [int(key) for key, value in entries]; assert keys == sorted(set(keys))
        owners = {int(key): value for key, value in entries}; links = 0
        for page in pdf:
            assert owners[int(pdf.xref_get_key(page.xref, 'StructParents')[1])].startswith('[')
            for link in page.get_links():
                links += 1; annotation = link['xref']; key = int(pdf.xref_get_key(annotation, 'StructParent')[1]); owner = int(owners[key].split()[0])
                assert pdf.xref_get_key(owner, 'S')[1] == '/Link'
                kids = pdf.xref_get_key(owner, 'K')[1]
                assert re.search(r'/Obj\s+' + str(annotation) + r'\s+0\s+R', kids)
                assert re.search(r'/Pg\s+' + str(page.xref) + r'\s+0\s+R', kids)
        assert links == 1
    if office:
        with ZipFile(directory / 'objects-office-edited.docx') as z:
            chart = ET.fromstring(z.read('word/charts/chart1.xml'))
            assert chart.find('.//c:numCache/c:pt/c:v', NS).text == '42'
            document = ET.fromstring(z.read('word/document.xml'))
            assert document.find('.//m:den//m:t', NS).text == '3'
            with ZipFile(io.BytesIO(z.read('word/embeddings/chart1.xlsx'))) as book:
                sheet = ET.fromstring(book.read('xl/worksheets/sheet1.xml'))
                assert sheet.find('.//s:c[@r="B2"]/s:v', NS).text == '42'
    files = sorted(p for p in directory.iterdir() if p.is_file() and p.suffix in ('.pdf', '.docx', '.svg', '.xml'))
    report = {'tolerancePoints': TOLERANCE, 'officeEditingChecked': office,
              'tools': {'PyMuPDF': fitz.VersionBind, 'HarfBuzz': hb.version_string()},
              'sha256': {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in files}}
    (directory / 'verification.json').write_text(json.dumps(report, indent=2) + '\n')
    print('P2 independent chart geometry, MATH baseline/rule, logical carriers and structure checks PASS' + ('; office edits PASS' if office else ''))


if __name__ == '__main__':
    p = argparse.ArgumentParser(description=__doc__); p.add_argument('artifacts', type=Path); p.add_argument('--office-edited', action='store_true'); args = p.parse_args()
    verify(args.artifacts, args.office_edited)
