"""Measure editable East Asian text, line geometry and an independent office reference."""
import argparse
import json
from pathlib import Path
from xml.etree import ElementTree as ET
from zipfile import ZipFile

import pypdf
import pymupdf

W = '{http://schemas.openxmlformats.org/wordprocessingml/2006/main}'
OPEN = set('（「『')
CLOSE = set('、。，．））」』！？')


def require(condition, message):
    if not condition:
        raise ValueError(message)


def document(path):
    with ZipFile(path) as package:
        xml = ET.fromstring(package.read('word/document.xml'))
    return ''.join(item.text or '' for item in xml.iter(W + 't'))


def inspect(path, expected, font):
    pdf = pymupdf.open(path); require(len(pdf) == 1, 'Expected a single PDF page.')
    page = pdf[0]
    require(any(font in entry[3] for entry in page.get_fonts()), 'Expected font face was not embedded.')
    lines = [line for block in page.get_text('rawdict')['blocks'] for line in block.get('lines', [])]
    strings = [''.join(char['c'] for span in line['spans'] for char in span['chars']) for line in lines]
    chars = [char for line in lines for span in line['spans'] for char in span['chars']]
    require(''.join(strings) == expected, 'PyMuPDF extraction does not retain the logical text.')
    require(''.join(pypdf.PdfReader(path).pages[0].extract_text().splitlines()) == expected, 'pypdf extraction does not retain the logical text.')
    require(len(strings) > 1 and all(text and text[0] not in CLOSE and text[-1] not in OPEN for text in strings), 'A prohibited character begins or ends a line.')
    return strings, [char['origin'] for char in chars]


def check(source, saved, pdf, reference, diagnostic, suffix):
    text = document(source)
    require(text and document(saved) == text, 'Editable source text changed.')
    lines, positions = inspect(pdf, text, 'RoadmapCJK' + suffix + 'Fixture')
    reference_lines, reference_positions = inspect(reference, text, 'NotoSansCJK' + suffix.lower() + '-Regular')
    max_x = max(abs(a[0] - b[0]) for a, b in zip(positions, reference_positions))
    max_y = max(abs(a[1] - b[1]) for a, b in zip(positions, reference_positions))
    passed = lines == reference_lines and max_x <= .5 and max_y <= .5
    codes = set(diagnostic.read_text().splitlines())
    if not passed:
        require('Layout.EastAsianMixedReference' in codes or 'Layout.EastAsianPunctuationReference' in codes, 'The reference mismatch lacks a diagnostic.')
    return {'language': suffix, 'textLength': len(text), 'lines': lines, 'referenceLines': reference_lines,
            'maxGlyphOriginDeltaXPt': max_x, 'maxGlyphOriginDeltaYPt': max_y,
            'geometryTolerancePt': .5, 'referenceFidelityPassed': passed, 'diagnostics': sorted(codes)}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('source', 'saved', 'pdf', 'reference', 'diagnostic'):
        parser.add_argument(name, type=Path)
    parser.add_argument('suffix', choices=('JP', 'SC', 'TC'))
    args = parser.parse_args()
    if pypdf.__version__ != '6.10.0' or pymupdf.VersionBind != '1.27.2.2':
        raise RuntimeError('Requires pypdf 6.10.0 and PyMuPDF 1.27.2.2.')
    print(json.dumps(check(args.source, args.saved, args.pdf, args.reference, args.diagnostic, args.suffix), ensure_ascii=False))


if __name__ == '__main__':
    main()
