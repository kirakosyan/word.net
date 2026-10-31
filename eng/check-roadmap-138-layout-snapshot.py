#!/usr/bin/env python3
"""Check preview glyph boxes against independent font metrics and PDF word boxes."""

import argparse
import json
import re
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path

from fontTools.pens.boundsPen import BoundsPen
from fontTools.svgLib.path import parse_path
from fontTools.ttLib import TTFont

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('input_docx', type=Path)
parser.add_argument('snapshot', type=Path)
parser.add_argument('svg', type=Path)
parser.add_argument('pdf_boxes', type=Path)
parser.add_argument('font', type=Path)
args = parser.parse_args()
w = '{http://schemas.openxmlformats.org/wordprocessingml/2006/main}'
s = '{http://www.w3.org/2000/svg}'
x = '{http://www.w3.org/1999/xhtml}'
with zipfile.ZipFile(args.input_docx) as document:
    root = ET.fromstring(document.read('word/document.xml'))
    assert [''.join(p.itertext()) for p in root.findall('.//' + w + 'body/' + w + 'p')] == ['Alpha', 'Beta']
fragments = json.loads(args.snapshot.read_text(encoding='utf-8'))
assert [item['text'] for item in fragments] == ['Alpha', 'Beta']
assert [item['path'] for item in fragments] == ['/document[1]/body[1]/p[1]', '/document[1]/body[1]/p[2]']
assert all(item['part'] == 'word/document.xml' and item['page'] == 1 and item['reason'] is None and not item['outside'] for item in fragments)
groups = ET.parse(args.svg).getroot().findall(s + 'g')
assert len(groups) == len(fragments)
pdf_words = ET.parse(args.pdf_boxes).findall('.//' + x + 'word')
assert [word.text for word in pdf_words] == ['Alpha', 'Beta']
font = TTFont(args.font)
cmap, outlines, glyphs = font.getBestCmap(), font['glyf'], font.getGlyphSet()
transform = re.compile(r'translate\(([-.0-9]+) ([-.0-9]+)\) scale\(([-.0-9]+) ([-.0-9]+)\)')
reported = []
for fragment, group, pdf_word in zip(fragments, groups, pdf_words):
    paths = group.findall(s + 'path')
    assert len(paths) == len(fragment['text'])
    boxes = []
    for path, character in zip(paths, fragment['text']):
        match = transform.fullmatch(path.get('transform', ''))
        assert match is not None
        tx, ty, sx, sy = map(float, match.groups())
        glyph = outlines[cmap[ord(character)]]
        font_bounds = BoundsPen(glyphs)
        glyphs[cmap[ord(character)]].draw(font_bounds)
        svg_bounds = BoundsPen(None)
        parse_path(path.get('d', ''), svg_bounds)
        assert font_bounds.bounds is not None and svg_bounds.bounds is not None
        assert all(abs(a - b) <= 0.001 for a, b in zip(font_bounds.bounds, svg_bounds.bounds))
        boxes.append((tx + glyph.xMin * sx, ty + glyph.yMax * sy,
                      tx + glyph.xMax * sx, ty + glyph.yMin * sy))
    independent = (min(box[0] for box in boxes), min(box[1] for box in boxes),
                   max(box[2] for box in boxes), max(box[3] for box in boxes))
    bounds = fragment['bounds']
    measured = (bounds['x'], bounds['y'], bounds['x'] + bounds['width'], bounds['y'] + bounds['height'])
    assert all(abs(a - b) <= 0.002 for a, b in zip(independent, measured))
    pdf = tuple(float(pdf_word.get(key)) for key in ('xMin', 'yMin', 'xMax', 'yMax'))
    assert pdf[0] - 0.002 <= measured[0] and pdf[1] - 0.002 <= measured[1]
    assert pdf[2] + 0.002 >= measured[2] and pdf[3] + 0.002 >= measured[3]
    reported.append({'text': fragment['text'], 'sourcePath': fragment['path'], 'bounds': measured, 'pdfWordBox': pdf})
print(json.dumps(reported))
