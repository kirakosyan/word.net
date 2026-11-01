#!/usr/bin/env python3
"""Check independently authored direct-border glyphs, strokes and saved OOXML."""
import argparse
import copy
import importlib.util
import json
from pathlib import Path
import xml.etree.ElementTree as ET
from zipfile import ZipFile

import fitz

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('plain_verifier', ROOT / 'eng/verify-roadmap-138-plain-tables.py')
plain = importlib.util.module_from_spec(spec)
spec.loader.exec_module(plain)
W = '{http://schemas.openxmlformats.org/wordprocessingml/2006/main}'
SIZES = (2, 3, 4, 6, 8, 12, 16, 20, 24)
POSITIVE = (('nil',) + tuple('implicit-%d' % size for size in SIZES if size % 4 == 0)
            + tuple('zero-%d' % size for size in SIZES if size % 2 == 0)
            + ('only-top', 'mixed-margins', 'empty-margin', 'explicit-108', 'explicit-115',
               'cell-zero', 'cell-108', 'cell-left', 'cell-right'))
CONTROLS = ('implicit-2', 'implicit-3', 'implicit-6', 'zero-3', 'two-rows', 'two-columns', 'horizontal-only', 'mixed-borders')
DIAGNOSED = ('long-wrap', 'multiple-paragraphs')
TOLERANCE = 0.25


def strokes(path):
    with fitz.open(path) as document:
        if len(document) != 1:
            raise ValueError('Expected one page')
        return [{'rect': list(item['rect']), 'width': item['width'], 'color': item['color']}
                for item in document[0].get_drawings()]


def borders(path):
    with ZipFile(path) as archive:
        root = ET.fromstring(archive.read('word/document.xml'))
    table, = root.iter(W + 'tbl')
    result = {}
    for side in table.find(W + 'tblPr/' + W + 'tblBorders'):
        style = side.get(W + 'val')
        result[side.tag.removeprefix(W)] = (style, None if style == 'nil' else (side.get(W + 'sz'), side.get(W + 'color')))
    return result


def same_glyphs(left, right, tolerance, message):
    if len(left) != len(right):
        raise ValueError(message + ': character count')
    for a, b in zip(left, right):
        if a['text'] != b['text'] or a['font'] != b['font'] or any(abs(a[axis] - b[axis]) > tolerance for axis in ('x', 'y')):
            raise ValueError(message + ': glyph or origin')


def same_strokes(left, right, tolerance, message):
    if len(left) != len(right):
        raise ValueError(message + ': stroke count')
    for a, b in zip(left, right):
        if a['color'] != b['color'] or abs(a['width'] - b['width']) > .005 or any(abs(x - y) > tolerance for x, y in zip(a['rect'], b['rect'])):
            raise ValueError(message + ': stroke geometry or color')


def check(values):
    result = {}
    for name in POSITIVE + CONTROLS + DIAGNOSED:
        item = values[name]
        source, parent, candidate, office = (item[key] for key in ('roles', 'parent', 'candidate', 'office'))
        if not source or any(len(value) != len(source) for value in (parent, candidate, office)):
            raise ValueError('Source and output character counts differ: ' + name)
        for (text, role), *observations in zip(source, parent, candidate, office):
            if role not in ('before', 'cell-default', 'cell-specified', 'after') or any(value['text'] != text or value['font'] != 'NotoSans-Regular' for value in observations):
                raise ValueError('Source text, reading order or pinned font differs: ' + name)
        delta = max(abs(a[axis] - b[axis]) for a, b in zip(candidate, office) for axis in ('x', 'y'))
        if name in POSITIVE:
            same_glyphs(candidate, office, TOLERANCE, 'Office comparison ' + name)
            same_strokes(item['candidateStrokes'], item['officeStrokes'], TOLERANCE, 'Office comparison ' + name)
            same_glyphs(candidate, item['saved'], .002, 'Saved output ' + name)
            same_strokes(item['candidateStrokes'], item['savedStrokes'], .002, 'Saved output ' + name)
            if item['sourceMargins'] != item['savedMargins'] or item['sourceBorders'] != item['savedBorders']:
                raise ValueError('Saved border or margin ownership changed: ' + name)
        elif name in CONTROLS:
            same_glyphs(parent, candidate, .002, 'Unqualified case changed ' + name)
            same_strokes(item['parentStrokes'], item['candidateStrokes'], .002, 'Unqualified case changed ' + name)
            if delta <= TOLERANCE:
                raise ValueError('Expected the unqualified reference difference to remain visible: ' + name)
        elif 'Table.BorderGeometry' not in item['diagnostics']:
            raise ValueError('Unqualified multiline geometry was not diagnosed: ' + name)
        result[name] = {'characters': len(source), 'officeMaximumGlyphOriginDeltaPt': delta,
                        'officeGlyphComparisonPassed': delta <= TOLERANCE,
                        'strokes': len(item['candidateStrokes']), 'savedGeometryMatches': name in POSITIVE}
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('sources', 'parent', 'candidate', 'office', 'saved'):
        parser.add_argument(name, type=Path)
    args = parser.parse_args()
    values = {}
    for name in POSITIVE + CONTROLS + DIAGNOSED:
        source, parent = args.sources / (name + '.docx'), args.parent / name / 'output.pdf'
        candidate, office = args.candidate / name / 'output.pdf', args.office / (name + '.pdf')
        item = {'roles': plain.source_roles(source), 'parent': plain.pdf_characters(parent),
                'candidate': plain.pdf_characters(candidate), 'office': plain.pdf_characters(office),
                'parentStrokes': strokes(parent), 'candidateStrokes': strokes(candidate), 'officeStrokes': strokes(office),
                'diagnostics': json.loads((args.candidate / name / 'diagnostics.json').read_text())}
        if name in POSITIVE:
            saved_docx, saved_pdf = args.saved / name / 'saved.docx', args.saved / name / 'saved.pdf'
            item.update({'saved': plain.pdf_characters(saved_pdf), 'savedStrokes': strokes(saved_pdf),
                         'sourceMargins': plain.source_margins(source), 'savedMargins': plain.source_margins(saved_docx),
                         'sourceBorders': borders(source), 'savedBorders': borders(saved_docx)})
        values[name] = item
    result = check(values)
    negatives = {}
    for name in ('wrong character', 'wrong font', 'shifted cell', 'shifted following text', 'shifted office',
                 'changed stroke position', 'changed stroke width', 'changed stroke color', 'missing stroke',
                 'changed saved glyph', 'changed saved stroke', 'changed border', 'changed margin owner', 'changed unqualified case', 'missing diagnostic'):
        altered = copy.deepcopy(values)
        item = altered['implicit-8']
        cell = next(i for i, role in enumerate(item['roles']) if role[1] == 'cell-default')
        after = next(i for i, role in enumerate(item['roles']) if role[1] == 'after')
        if name == 'wrong character': item['candidate'][cell]['text'] = '?'
        if name == 'wrong font': item['candidate'][cell]['font'] = 'other'
        if name == 'shifted cell': item['candidate'][cell]['x'] += 1
        if name == 'shifted following text': item['candidate'][after]['y'] += 1
        if name == 'shifted office': item['office'][cell]['x'] += 2
        if name == 'changed stroke position': item['candidateStrokes'][0]['rect'][1] += 1
        if name == 'changed stroke width': item['candidateStrokes'][0]['width'] += 1
        if name == 'changed stroke color': item['candidateStrokes'][0]['color'] = (1, 0, 0)
        if name == 'missing stroke': item['candidateStrokes'].pop()
        if name == 'changed saved glyph': item['saved'][cell]['x'] += 1
        if name == 'changed saved stroke': item['savedStrokes'][0]['rect'][1] += 1
        if name == 'changed border': item['savedBorders']['top'] = ('single', ('16', '000000'))
        if name == 'changed margin owner': altered['mixed-margins']['savedMargins']['cells'][0] = None
        if name == 'changed unqualified case': altered['two-rows']['candidate'][0]['x'] += 1
        if name == 'missing diagnostic': altered['long-wrap']['diagnostics'] = []
        try:
            check(altered)
        except (ValueError, KeyError, TypeError, IndexError) as error:
            negatives[name] = str(error)
        else:
            raise ValueError('Negative control unexpectedly passed: ' + name)
    print(json.dumps({'glyphAndStrokeTolerancePt': TOLERANCE, 'cases': result, 'negativeControls': negatives,
                      'controlMethod': 'Parsed glyph, stroke and OOXML observations changed in memory.'}, indent=2))


if __name__ == '__main__':
    main()
