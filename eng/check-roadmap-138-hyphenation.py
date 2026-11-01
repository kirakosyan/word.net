"""Independently inspect editable text, extraction, break points and hyphen outlines."""
import argparse
import json
import re
import subprocess
from pathlib import Path
from xml.etree import ElementTree as ET
from zipfile import ZipFile

from fontTools.pens.boundsPen import BoundsPen
from fontTools.ttLib import TTFont
import pypdf
import pymupdf
import pyphen

W = '{http://schemas.openxmlformats.org/wordprocessingml/2006/main}'
EXPECTED = 'A extraordinary example'


def text(path):
    with ZipFile(path) as package:
        root = ET.fromstring(package.read('word/document.xml'))
    return ''.join(element.text or '' for element in root.iter(W + 't'))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source', type=Path); parser.add_argument('saved', type=Path)
    parser.add_argument('pdf', type=Path); parser.add_argument('font', type=Path)
    args = parser.parse_args()
    if pypdf.__version__ != '6.10.0' or pymupdf.VersionBind != '1.27.2.2' or pyphen.__version__ != '0.17.2':
        raise RuntimeError('Requires pinned pypdf, PyMuPDF and Pyphen versions.')
    if text(args.source) != EXPECTED or text(args.saved) != EXPECTED:
        raise ValueError('Editable source text changed.')
    positions = pyphen.Pyphen(lang='en_US').positions('extraordinary')
    if positions != [2, 5, 7, 9]:
        raise ValueError('Independent dictionary break points changed.')
    reader = pypdf.PdfReader(args.pdf)
    if len(reader.pages) != 1:
        raise ValueError('Expected one physical page.')
    plain = reader.pages[0].extract_text()
    poppler = subprocess.run(['pdftotext', '-raw', str(args.pdf), '-'], capture_output=True, check=True).stdout.decode('utf-8')
    pdf = pymupdf.open(args.pdf); page = pdf[0]; pymu = page.get_text()
    if any(' '.join(value.split()) != EXPECTED or 'extraordinary' not in value or '-' in value for value in (plain, poppler, pymu)):
        raise ValueError('Extracted logical text changed or contains the display hyphen.')
    matches = page.search_for('extraordinary')
    if len(matches) != 1:
        raise ValueError('The source word is not independently searchable once.')
    paths = page.get_drawings()
    if len(paths) != len('extraordinary') + 1:
        raise ValueError('Visible word or display-hyphen outlines are missing.')
    first = [path['rect'] for path in paths if path['rect'].y0 < 50]
    second = [path['rect'] for path in paths if path['rect'].y0 >= 50]
    if len(first) - 1 not in positions or len(second) != len('extraordinary') - (len(first) - 1):
        raise ValueError('The visible break does not match the independent dictionary.')
    hyphen = max(first, key=lambda rect: rect.x0)
    font = TTFont(args.font); glyphs = font.getGlyphSet(); name = font.getBestCmap()[ord('-')]
    pen = BoundsPen(glyphs); glyphs[name].draw(pen); x0, y0, x1, y1 = pen.bounds
    units = font['head'].unitsPerEm; expected = ((x1 - x0) * 11 / units, (y1 - y0) * 11 / units)
    if abs(hyphen.width - expected[0]) > 0.05 or abs(hyphen.height - expected[1]) > 0.05:
        raise ValueError('The visible hyphen outline does not match the font.')
    if matches[0].y1 >= min(rect.y0 for rect in second):
        raise ValueError('The recorded first-line search-highlight limitation changed.')
    print(json.dumps({'dictionaryPoints': positions, 'visibleBreak': len(first) - 1,
                      'sourceText': EXPECTED, 'readerTextsAgreeAfterWhitespaceNormalization': True,
                      'hyphenBoundsPt': list(hyphen), 'fontHyphenSizePt': expected,
                      'searchBoundsPt': list(matches[0]), 'secondLineTopPt': min(rect.y0 for rect in second),
                      'searchGeometryQualified': False, 'glyphTolerancePt': 0.05}))


if __name__ == '__main__':
    main()
