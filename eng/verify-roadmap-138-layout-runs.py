#!/usr/bin/env python3
"""Independently check direct source-run owners and emitted glyph geometry."""
import argparse
import copy
import hashlib
import importlib.util
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

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('cell_reference', ROOT / 'eng/verify-roadmap-138-layout-cells.py')
cells = importlib.util.module_from_spec(spec)
spec.loader.exec_module(cells)
W = '{http://schemas.openxmlformats.org/wordprocessingml/2006/main}'
SVG = '{http://www.w3.org/2000/svg}'
TRANSFORM = re.compile(r'translate\(([-.0-9]+) ([-.0-9]+)\) scale\(([-.0-9]+) ([-.0-9]+)\)')
FONT_SHA = 'b85c38ecea8a7cfb39c24e395a4007474fa5a4fc864f6ee33309eb4948d232d5'


def read(source, output):
    with ZipFile(source) as package:
        document = ET.fromstring(package.read('word/document.xml'))
    body = document.find(W + 'body')
    paragraphs = body.findall(W + 'p')
    tables = body.findall(W + 'tbl')
    if len(paragraphs) != 3 or len(tables) != 1:
        raise ValueError('Unexpected source document structure')
    first, joined = (paragraph.findall(W + 'r') for paragraph in paragraphs[:2])
    cell_paragraph = tables[0].find('./' + W + 'tr/' + W + 'tc/' + W + 'p')
    cell_runs = cell_paragraph.findall(W + 'r')
    hyperlink = paragraphs[2].find(W + 'hyperlink')
    wrapped_runs = hyperlink.findall(W + 'r')
    if len(first) != 2 or len(joined) != 2 or len(cell_runs) != 1 or len(wrapped_runs) != 1:
        raise ValueError('Unexpected direct or wrapped run count')
    color = lambda run: run.find('./' + W + 'rPr/' + W + 'color').get(W + 'val')
    if color(first[0]) == color(first[1]) or color(joined[0]) != color(joined[1]):
        raise ValueError('Expected distinct direct styles and matching coalesced styles')
    text = lambda run: ''.join(node.text or '' for node in run.findall(W + 't'))
    p1, p2, p3 = ('/document[1]/body[1]/p[1]', '/document[1]/body[1]/p[2]', '/document[1]/body[1]/p[3]')
    cell = '/document[1]/body[1]/tbl[1]/tr[1]/tc[1]'
    expected = [(p1, text(first[0]), p1 + '/r[1]', None), (p1, text(first[1]), p1 + '/r[2]', None),
                (p2, ''.join(map(text, joined)), None, None), (cell + '/p[1]', text(cell_runs[0]), cell + '/p[1]/r[1]', cell)]
    expected.extend((p3, character, None, None) for character in text(wrapped_runs[0]))
    fragments = json.loads((output / 'snapshot.json').read_text(encoding='utf-8'))
    with fitz.open(output / 'output.pdf') as pdf:
        if len(pdf) != 1:
            raise ValueError('Expected one PDF page')
        chars = [character for block in pdf[0].get_text('rawdict')['blocks'] for line in block.get('lines', [])
                 for span in line['spans'] for character in span['chars']]
    root = ET.parse(output / 'page-1.svg').getroot()
    groups = [group for group in root.findall(SVG + 'g') if group.find(SVG + 'title') is not None]
    return expected, fragments, groups, chars


def check(expected, fragments, groups, chars, font):
    if not expected or len(expected) != len(fragments) or len(fragments) != len(groups):
        raise ValueError('Source, snapshot or SVG fragment count differs')
    cmap, outlines, glyphs = font.getBestCmap(), font['glyf'], font.getGlyphSet()
    offset, maximum_delta = 0, 0
    for index, (wanted, item, group) in enumerate(zip(expected, fragments, groups)):
        path, text, run, cell = wanted
        if item['page'] != 1 or item['order'] != index or item['part'] != 'word/document.xml' or item['path'] != path or item['text'] != text:
            raise ValueError('Source paragraph, order or text changed')
        if item['runPath'] != run or item['runPart'] != ('word/document.xml' if run else None):
            raise ValueError('Source run identity is missing or ambiguous')
        if item['cellPath'] != cell or item['cellPart'] != ('word/document.xml' if cell else None):
            raise ValueError('Cell identity differs from source XML')
        if item['reason'] is not None or item['outside'] or group.find(SVG + 'title').text != text:
            raise ValueError('Unexpected geometry status or SVG text')
        paths = group.findall(SVG + 'path')
        subset = chars[offset:offset + len(text)]
        offset += len(text)
        if len(paths) != len(text) or ''.join(c['c'] for c in subset) != text:
            raise ValueError('PDF/SVG glyph text differs from source')
        boxes = []
        for path_element, character in zip(paths, text):
            match = TRANSFORM.fullmatch(path_element.get('transform', ''))
            if match is None:
                raise ValueError('Unexpected SVG glyph transform')
            tx, ty, sx, sy = map(float, match.groups())
            name = cmap[ord(character)]
            glyph = outlines[name]
            font_bounds, svg_bounds = BoundsPen(glyphs), BoundsPen(None)
            glyphs[name].draw(font_bounds)
            parse_path(path_element.get('d', ''), svg_bounds)
            if font_bounds.bounds is None or svg_bounds.bounds is None or any(abs(a - b) > .001 for a, b in zip(font_bounds.bounds, svg_bounds.bounds)):
                raise ValueError('SVG outline differs from pinned font')
            boxes.append((tx + glyph.xMin * sx, ty + glyph.yMax * sy, tx + glyph.xMax * sx, ty + glyph.yMin * sy))
        expected_box = (min(b[0] for b in boxes), min(b[1] for b in boxes), max(b[2] for b in boxes), max(b[3] for b in boxes))
        bounds = item['bounds']
        if bounds is None:
            raise ValueError('A qualified glyph fragment lost its bounds')
        actual = (bounds['x'], bounds['y'], bounds['x'] + bounds['width'], bounds['y'] + bounds['height'])
        if not all(math.isfinite(value) for value in actual):
            raise ValueError('Non-finite snapshot bounds')
        delta = max(abs(a - b) for a, b in zip(expected_box, actual))
        maximum_delta = max(maximum_delta, delta)
        if delta > .002:
            raise ValueError('Snapshot and independent SVG/font bounds differ')
        pdf_box = (min(c['bbox'][0] for c in subset), min(c['bbox'][1] for c in subset),
                   max(c['bbox'][2] for c in subset), max(c['bbox'][3] for c in subset))
        if pdf_box[0] - .002 > actual[0] or pdf_box[1] - .002 > actual[1] or pdf_box[2] + .002 < actual[2] or pdf_box[3] + .002 < actual[3]:
            raise ValueError('PDF character boxes do not contain snapshot ink')
    if offset != len(chars):
        raise ValueError('Unmatched PDF characters')
    return {'pages': 1, 'fragments': len(fragments), 'mappedRuns': sum(item['runPath'] is not None for item in fragments),
            'unmappedFragments': sum(item['runPath'] is None for item in fragments), 'maximumFontSvgBoundsDeltaPt': maximum_delta}


def office_segments(actual_path, reference_path, expected):
    def characters(path):
        with fitz.open(path) as document:
            if len(document) != 1:
                return []
            return [character for block in document[0].get_text('rawdict')['blocks'] for line in block.get('lines', [])
                    for span in line['spans'] for character in span['chars']]
    left, right = characters(actual_path), characters(reference_path)
    combined = ''.join(item[1] for item in expected)
    if ''.join(item['c'] for item in left) != combined or ''.join(item['c'] for item in right) != combined:
        return {'textOrderAgrees': False, 'segments': []}
    lengths = [len(item[1]) for item in expected[:4]] + [sum(len(item[1]) for item in expected[4:])]
    output, start = [], 0
    for name, length in zip(('first direct run', 'second direct run', 'coalesced runs', 'table cell', 'wrapped hyperlink'), lengths):
        end = start + length
        delta = max(max(abs(a - b) for a, b in zip(x['origin'], y['origin'])) for x, y in zip(left[start:end], right[start:end]))
        output.append({'name': name, 'characters': length, 'maximumGlyphOriginDeltaPt': delta, 'glyphComparisonPassed': delta <= .25})
        start = end
    return {'textOrderAgrees': True, 'glyphOriginTolerancePt': .25, 'segments': output}


def read_namespace_case(source, output, name):
    with ZipFile(source) as package:
        document = ET.fromstring(package.read('word/document.xml'))
    body = document.find(W + 'body')
    if name == 'namespace-run':
        parent, local, diagnostic = body.find(W + 'p'), 'r', 'Docx.Inline'
    else:
        parent, local, diagnostic = body, 'p', 'Docx.Block'
    siblings = [element for element in parent if element.tag.split('}')[-1] == local]
    namespaces = {element.tag.split('}')[0] for element in siblings}
    if len(siblings) != 2 or len(namespaces) != 2 or W + local not in [element.tag for element in siblings]:
        raise ValueError('Source does not contain the intended namespace collision')
    fragments = json.loads((output / 'snapshot.json').read_text(encoding='utf-8'))
    diagnostics = json.loads((output / 'diagnostics.json').read_text(encoding='utf-8'))
    with fitz.open(output / 'output.pdf') as pdf:
        if len(pdf) != 1:
            raise ValueError('Expected one PDF page for the namespace negative')
        text = ''.join(character['c'] for block in pdf[0].get_text('rawdict')['blocks'] for line in block.get('lines', [])
                       for span in line['spans'] for character in span['chars'])
    root = ET.parse(output / 'page-1.svg').getroot()
    titles = [group.find(SVG + 'title').text for group in root.findall(SVG + 'g') if group.find(SVG + 'title') is not None]
    return {'fragments': fragments, 'diagnostics': diagnostics, 'expectedDiagnostic': diagnostic, 'pdfText': text, 'svgTitles': titles}


def check_namespace_case(value):
    if len(value['fragments']) != 1 or value['expectedDiagnostic'] not in value['diagnostics']:
        raise ValueError('Namespace conflict or its diagnostic disappeared')
    item = value['fragments'][0]
    if item['text'] != 'MMMM' or value['pdfText'] != 'MMMM' or value['svgTitles'] != ['MMMM']:
        raise ValueError('Namespace-negative visible text changed')
    if item['runPath'] is not None or item['runPart'] is not None or item['bounds'] is None or item['reason'] is not None:
        raise ValueError('Ambiguous namespace path was assigned a run owner')
    return {'runSource': None, 'diagnostic': value['expectedDiagnostic'], 'visibleTextPreserved': True}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source', type=Path)
    parser.add_argument('output', type=Path)
    parser.add_argument('font', type=Path)
    parser.add_argument('--office', type=Path)
    parser.add_argument('--namespace-sources', type=Path)
    parser.add_argument('--namespace-outputs', type=Path)
    args = parser.parse_args()
    digest = hashlib.sha256(args.font.read_bytes()).hexdigest()
    if digest != FONT_SHA:
        raise ValueError('Unexpected font identity')
    font = TTFont(args.font)
    values = read(args.source, args.output)
    result = check(*values, font)
    negatives = {}
    for name in ('wrong run path', 'mapped coalesced run', 'mapped wrapped run', 'changed source text',
                 'shifted bounds', 'moved SVG glyph', 'changed PDF character', 'wrong cell path'):
        expected, fragments, groups, chars = copy.deepcopy(values)
        if name == 'wrong run path': fragments[0]['runPath'] += '/changed'
        if name == 'mapped coalesced run': fragments[2]['runPath'] = '/wrong'
        if name == 'mapped wrapped run': fragments[4]['runPath'] = '/wrong'
        if name == 'changed source text': expected[0] = (expected[0][0], 'wrong', expected[0][2], expected[0][3])
        if name == 'shifted bounds': fragments[0]['bounds']['x'] += 2
        if name == 'moved SVG glyph': groups[0].find(SVG + 'path').set('transform', 'translate(0 0) scale(0.011 -0.011)')
        if name == 'changed PDF character': chars[0]['c'] = '?'
        if name == 'wrong cell path': fragments[3]['cellPath'] += '/changed'
        try:
            check(expected, fragments, groups, chars, font)
        except (ValueError, KeyError, TypeError, AttributeError) as error:
            negatives[name] = str(error)
        else:
            raise ValueError('Negative control unexpectedly passed: ' + name)
    if (args.namespace_sources is None) != (args.namespace_outputs is None):
        parser.error('Provide both namespace directories or neither.')
    namespace_results = {}
    if args.namespace_sources is not None:
        for name in ('namespace-run', 'namespace-paragraph'):
            value = read_namespace_case(args.namespace_sources / (name + '.docx'), args.namespace_outputs / name, name)
            namespace_results[name] = check_namespace_case(value)
            altered = copy.deepcopy(value)
            altered['fragments'][0]['runPath'] = '/wrong'
            try:
                check_namespace_case(altered)
            except ValueError as error:
                negatives['mapped ' + name] = str(error)
            else:
                raise ValueError('Namespace negative control unexpectedly passed: ' + name)
    office = None if args.office is None else {'wholePage': cells.compare_office(args.output / 'output.pdf', args.office),
                                                'segments': office_segments(args.output / 'output.pdf', args.office, values[0])}
    print(json.dumps({'fontSha256': digest, 'specimen': result, 'namespaceNegatives': namespace_results, 'negativeControls': negatives,
                      'controlMethod': 'Parsed source/output measurements changed in memory.', 'officeComparison': office}, indent=2))


if __name__ == '__main__':
    main()
