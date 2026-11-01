#!/usr/bin/env python3
"""Independently check source-cell identities, SVG glyph ink and PDF word boxes."""
import argparse
import copy
import hashlib
import json
import math
from pathlib import Path
import re
import xml.etree.ElementTree as ET
from zipfile import ZipFile

import fitz
from fontTools.pens.boundsPen import BoundsPen
from fontTools.svgLib.path import parse_path
from fontTools.ttLib import TTFont

W = '{http://schemas.openxmlformats.org/wordprocessingml/2006/main}'
SVG = '{http://www.w3.org/2000/svg}'
FONT_SHA = 'b85c38ecea8a7cfb39c24e395a4007474fa5a4fc864f6ee33309eb4948d232d5'
TRANSFORM = re.compile(r'translate\(([-.0-9]+) ([-.0-9]+)\) scale\(([-.0-9]+) ([-.0-9]+)\)')


def read(source, output):
    with ZipFile(source) as package:
        document = ET.fromstring(package.read('word/document.xml'))
    tables = document.findall('./' + W + 'body/' + W + 'tbl')
    if len(tables) != 1:
        raise ValueError('Expected one direct source table')
    expected = {}
    for row_index, row in enumerate(tables[0].findall(W + 'tr'), 1):
        for cell_index, cell in enumerate(row.findall(W + 'tc'), 1):
            paragraphs = cell.findall(W + 'p')
            if len(paragraphs) != 1:
                raise ValueError('Expected one source paragraph per cell')
            path = f'/document[1]/body[1]/tbl[1]/tr[{row_index}]/tc[{cell_index}]'
            expected[path] = ''.join(node.text or '' for node in paragraphs[0].iter(W + 't'))
    fragments = json.loads((output / 'snapshot.json').read_text(encoding='utf-8'))
    with fitz.open(output / 'output.pdf') as pdf:
        words = [page.get_text('words') for page in pdf]
    groups = []
    for page in range(1, len(words) + 1):
        root = ET.parse(output / f'page-{page}.svg').getroot()
        groups.append([group for group in root.findall(SVG + 'g') if group.find(SVG + 'title') is not None])
    return expected, fragments, groups, words


def check(expected, fragments, groups, words, font):
    if not expected or not words or len(groups) != len(words):
        raise ValueError('Missing source cells or output pages')
    cmap, outlines, glyphs = font.getBestCmap(), font['glyf'], font.getGlyphSet()
    counts = {path: 0 for path in expected}
    maximum_delta = 0
    for page_index, (page_groups, page_words) in enumerate(zip(groups, words), 1):
        items = [item for item in fragments if item['page'] == page_index]
        if len(items) != len(page_groups) or len(items) != len(page_words):
            raise ValueError('Page text or fragment count changed')
        for item, group, word in zip(items, page_groups, page_words):
            cell = item['cellPath']
            if cell not in expected or item['text'] != expected[cell] or item['path'] != cell + '/p[1]':
                raise ValueError('Cell source or text no longer matches independent XML')
            if item['part'] != 'word/document.xml' or item['cellPart'] != 'word/document.xml' or item['reason'] is not None or item['outside']:
                raise ValueError('Unexpected source, page boundary or unavailable geometry')
            counts[cell] += 1
            if group.find(SVG + 'title').text != item['text'] or word[4] != item['text']:
                raise ValueError('PDF/SVG text order differs from the snapshot')
            paths = group.findall(SVG + 'path')
            if len(paths) != len(item['text']):
                raise ValueError('SVG glyph count changed')
            boxes = []
            for path, character in zip(paths, item['text']):
                match = TRANSFORM.fullmatch(path.get('transform', ''))
                if match is None:
                    raise ValueError('Unexpected SVG glyph transform')
                tx, ty, sx, sy = map(float, match.groups())
                name = cmap[ord(character)]
                glyph = outlines[name]
                font_bounds, svg_bounds = BoundsPen(glyphs), BoundsPen(None)
                glyphs[name].draw(font_bounds)
                parse_path(path.get('d', ''), svg_bounds)
                if font_bounds.bounds is None or svg_bounds.bounds is None or any(abs(a - b) > .001 for a, b in zip(font_bounds.bounds, svg_bounds.bounds)):
                    raise ValueError('SVG outline differs from the pinned font')
                boxes.append((tx + glyph.xMin * sx, ty + glyph.yMax * sy,
                              tx + glyph.xMax * sx, ty + glyph.yMin * sy))
            expected_box = (min(box[0] for box in boxes), min(box[1] for box in boxes),
                            max(box[2] for box in boxes), max(box[3] for box in boxes))
            bounds = item['bounds']
            if bounds is None:
                raise ValueError('A qualified cell lost its glyph bounds')
            actual = (bounds['x'], bounds['y'], bounds['x'] + bounds['width'], bounds['y'] + bounds['height'])
            if not all(math.isfinite(value) for value in actual):
                raise ValueError('Non-finite snapshot bounds')
            delta = max(abs(a - b) for a, b in zip(expected_box, actual))
            maximum_delta = max(maximum_delta, delta)
            if delta > .002:
                raise ValueError('Snapshot and independent SVG/font bounds differ')
            if word[0] - .002 > actual[0] or word[1] - .002 > actual[1] or word[2] + .002 < actual[2] or word[3] + .002 < actual[3]:
                raise ValueError('PDF word box does not contain snapshot glyph ink')
    if len(fragments) != sum(counts.values()):
        raise ValueError('Snapshot contains an unselected page')
    for cell, count in counts.items():
        expected_count = len(words) if '/tr[1]/' in cell else 1
        if count != expected_count:
            raise ValueError('Cell or repeated-header occurrence count changed')
    return {'pages': len(words), 'sourceCells': len(expected), 'fragments': len(fragments),
            'repeatedHeaderOccurrences': len(words), 'maximumFontSvgBoundsDeltaPt': maximum_delta}


def compare_office(actual_path, reference_path):
    def page_data(document, index):
        if index >= len(document):
            return [], []
        spans = [span for block in document[index].get_text('rawdict')['blocks']
                 for line in block.get('lines', []) for span in line['spans']]
        return [(character['c'], character['origin']) for span in spans for character in span['chars']], sorted({span['font'] for span in spans})

    pages = []
    with fitz.open(actual_path) as actual, fitz.open(reference_path) as reference:
        for index in range(max(len(actual), len(reference))):
            left, left_fonts = page_data(actual, index)
            right, right_fonts = page_data(reference, index)
            same = bool(left) and bool(right) and [value[0] for value in left] == [value[0] for value in right]
            delta = max((max(abs(a - b) for a, b in zip(x[1], y[1])) for x, y in zip(left, right)), default=0) if same else None
            comparable = left_fonts == right_fonts
            pages.append({'page': index + 1, 'actualCharacters': len(left), 'referenceCharacters': len(right),
                          'actualFonts': left_fonts, 'referenceFonts': right_fonts, 'textOrderAgrees': same, 'fontNamesAgree': comparable,
                          'maximumGlyphOriginDeltaPt': delta, 'glyphComparisonPassed': comparable and same and delta <= .25})
        return {'actualPages': len(actual), 'referencePages': len(reference), 'glyphOriginTolerancePt': .25,
                'glyphComparisonPassed': len(actual) == len(reference) and all(page['glyphComparisonPassed'] for page in pages),
                'pages': pages}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('sources', type=Path)
    parser.add_argument('outputs', type=Path)
    parser.add_argument('font', type=Path)
    parser.add_argument('--office', type=Path, help='independent office PDF directory, if captured')
    args = parser.parse_args()
    digest = hashlib.sha256(args.font.read_bytes()).hexdigest()
    if digest != FONT_SHA:
        raise ValueError('Unexpected font identity')
    font = TTFont(args.font)
    inputs, results = {}, {}
    for name in ('cells', 'headers'):
        values = read(args.sources / (name + '.docx'), args.outputs / name)
        inputs[name] = values
        results[name] = check(*values, font)
    if results['headers']['pages'] < 2:
        raise ValueError('Expected a repeated header on multiple pages')
    negatives = {}
    for name in ('wrong cell path', 'changed source text', 'shifted bounds', 'moved SVG glyph', 'missing PDF word', 'missing repeated header'):
        values = copy.deepcopy(inputs['headers' if name == 'missing repeated header' else 'cells'])
        expected, fragments, groups, words = values
        if name == 'wrong cell path': fragments[0]['cellPath'] += '/changed'
        if name == 'changed source text': expected[fragments[0]['cellPath']] = 'changed'
        if name == 'shifted bounds': fragments[0]['bounds']['x'] += 2
        if name == 'moved SVG glyph': groups[0][0].find(SVG + 'path').set('transform', 'translate(0 0) scale(0.011 -0.011)')
        if name == 'missing PDF word': words[0].pop(0)
        if name == 'missing repeated header': fragments.pop(next(i for i, item in enumerate(fragments) if item['page'] == 2 and '/tr[1]/' in item['cellPath']))
        try:
            check(*values, font)
        except (ValueError, KeyError, TypeError, AttributeError) as error:
            negatives[name] = str(error)
        else:
            raise ValueError('Negative control unexpectedly passed: ' + name)
    office = None if args.office is None else {
        name: compare_office(args.outputs / name / 'output.pdf', args.office / (name + '.pdf'))
        for name in ('cells', 'headers')}
    print(json.dumps({'fontSha256': digest, 'specimens': results, 'negativeControls': negatives,
                      'controlMethod': 'Parsed source/output measurements changed in memory.', 'officeComparison': office}, indent=2))


if __name__ == '__main__':
    main()
