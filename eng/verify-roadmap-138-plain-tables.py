#!/usr/bin/env python3
"""Compare source-labelled glyphs with the previous build and a pinned office reader."""
import argparse
import copy
import json
from pathlib import Path
import xml.etree.ElementTree as ET
from zipfile import ZipFile

import fitz

W = '{http://schemas.openxmlformats.org/wordprocessingml/2006/main}'
NEW = ('only-top', 'partial-mixed', 'empty-table', 'empty-cell', 'left-only')
POSITIVE = ('implicit', 'two-columns', 'two-rows', 'explicit-115', 'explicit-108', 'explicit-zero', 'cell-zero') + NEW
CONTROLS = ('border', 'invalid-metadata')
TOLERANCE = 0.25


def source_roles(path):
    with ZipFile(path) as archive:
        root = ET.fromstring(archive.read('word/document.xml'))
    body = root.find(W + 'body')
    roles, seen_table = [], False
    for child in body:
        if child.tag == W + 'tbl':
            seen_table = True
            table_margin = child.find(W + 'tblPr/' + W + 'tblCellMar/' + W + 'left')
            for row in child.findall(W + 'tr'):
                for cell in row.findall(W + 'tc'):
                    cell_margin = cell.find(W + 'tcPr/' + W + 'tcMar/' + W + 'left')
                    role = 'cell-specified' if cell_margin is not None or table_margin is not None else 'cell-default'
                    for text in cell.iter(W + 't'):
                        roles.extend((character, role) for character in (text.text or ''))
        elif child.tag == W + 'p':
            for text in child.iter(W + 't'):
                roles.extend((character, 'after' if seen_table else 'before') for character in (text.text or ''))
    return roles


def pdf_characters(path):
    result = []
    with fitz.open(path) as document:
        if len(document) != 1:
            raise ValueError('Expected one page in this bounded sample')
        for block in document[0].get_text('rawdict')['blocks']:
            for line in block.get('lines', []):
                for span in line['spans']:
                    for character in span['chars']:
                        result.append({'text': character['c'], 'font': span['font'], 'x': character['origin'][0], 'y': character['origin'][1]})
    return result


def source_margins(path):
    with ZipFile(path) as archive:
        root = ET.fromstring(archive.read('word/document.xml'))
    table = list(root.iter(W + 'tbl'))
    if len(table) != 1:
        raise ValueError('Expected one source table')
    def sides(element):
        return None if element is None else {child.tag.removeprefix(W): (child.get(W + 'w'), child.get(W + 'type')) for child in element}
    return {'table': sides(table[0].find(W + 'tblPr/' + W + 'tblCellMar')),
            'cells': [sides(cell.find(W + 'tcPr/' + W + 'tcMar')) for cell in table[0].iter(W + 'tc')]}


def check(values):
    output = {}
    for name in POSITIVE + CONTROLS:
        item = values[name]
        roles, baseline, candidate, reference = (item[key] for key in ('roles', 'baseline', 'candidate', 'reference'))
        if not roles or len(roles) != len(baseline) or len(roles) != len(candidate) or len(roles) != len(reference):
            raise ValueError('Source and output character counts differ: ' + name)
        if name in POSITIVE:
            saved = item['saved']
            if len(saved) != len(candidate):
                raise ValueError('Saved output character count differs: ' + name)
            for original, restored in zip(candidate, saved):
                if original['text'] != restored['text'] or original['font'] != restored['font'] or any(
                        abs(original[axis] - restored[axis]) > .002 for axis in ('x', 'y')):
                    raise ValueError('Saved output changes glyphs or geometry: ' + name)
            if item['savedMargins'] != item['sourceMargins']:
                raise ValueError('Saved margin omission or explicit values differ: ' + name)
        max_reference, max_change = 0, 0
        for (expected, role), before, after, office in zip(roles, baseline, candidate, reference):
            if role not in ('before', 'cell-default', 'cell-specified', 'after') or any(c['text'] != expected for c in (before, after, office)):
                raise ValueError('Text, order or source role differs: ' + name)
            if any(c['font'] != 'NotoSans-Regular' for c in (before, after, office)):
                raise ValueError('Pinned font differs: ' + name)
            dx, dy = after['x'] - before['x'], after['y'] - before['y']
            wanted_x = 5.75 if name in NEW and role == 'cell-default' else 0
            wanted_y = -6 if name in NEW and role == 'after' else 0
            if abs(dx - wanted_x) > .002 or abs(dy - wanted_y) > .002:
                raise ValueError('Unexpected source-bounded geometry change: ' + name)
            max_change = max(max_change, abs(dx), abs(dy))
            max_reference = max(max_reference, abs(after['x'] - office['x']), abs(after['y'] - office['y']))
        if name in POSITIVE and max_reference > TOLERANCE:
            raise ValueError('Office glyph-origin tolerance exceeded: ' + name)
        output[name] = {'characters': len(roles), 'maximumChangePt': max_change,
                        'officeMaximumGlyphOriginDeltaPt': max_reference,
                        'officeGlyphComparisonPassed': max_reference <= TOLERANCE,
                        'savedGeometryMatches': True if name in POSITIVE else None}
    return output


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('sources', type=Path)
    parser.add_argument('baseline', type=Path)
    parser.add_argument('candidate', type=Path)
    parser.add_argument('office', type=Path)
    parser.add_argument('saved', type=Path)
    args = parser.parse_args()
    values = {name: {'roles': source_roles(args.sources / (name + '.docx')),
                     'baseline': pdf_characters(args.baseline / name / 'output.pdf'),
                     'candidate': pdf_characters(args.candidate / name / 'output.pdf'),
                     'reference': pdf_characters(args.office / (name + '.pdf')),
                     'saved': pdf_characters(args.saved / name / 'saved.pdf') if name in POSITIVE else None,
                     'sourceMargins': source_margins(args.sources / (name + '.docx')) if name in POSITIVE else None,
                     'savedMargins': source_margins(args.saved / name / 'saved.docx') if name in POSITIVE else None}
              for name in POSITIVE + CONTROLS}
    result = check(values)
    negatives = {}
    for name in ('wrong text', 'wrong font', 'shifted cell', 'shifted following text', 'changed plain body',
                 'changed explicit margin', 'changed border', 'changed partial margin', 'shifted office reference', 'wrong source role', 'missing character',
                 'changed saved glyph', 'introduced default margin', 'changed saved margin', 'changed override owner', 'changed invalid metadata case'):
        altered = copy.deepcopy(values)
        positive, explicit, border = (altered[key] for key in ('only-top', 'explicit-zero', 'border'))
        cell = next(i for i, role in enumerate(positive['roles']) if role[1] == 'cell-default')
        after = next(i for i, role in enumerate(positive['roles']) if role[1] == 'after')
        if name == 'wrong text': positive['candidate'][cell]['text'] = '?'
        if name == 'wrong font': positive['candidate'][cell]['font'] = 'other'
        if name == 'shifted cell': positive['candidate'][cell]['x'] += 1
        if name == 'shifted following text': positive['candidate'][after]['y'] += 1
        if name == 'changed plain body': positive['candidate'][0]['x'] += 1
        if name == 'changed explicit margin': explicit['candidate'][0]['x'] += 1
        if name == 'changed border': border['candidate'][0]['x'] += 1
        if name == 'changed partial margin': altered['partial-mixed']['candidate'][0]['x'] += 1
        if name == 'shifted office reference': positive['reference'][cell]['x'] += 2
        if name == 'wrong source role': positive['roles'][cell] = (positive['roles'][cell][0], 'before')
        if name == 'missing character': positive['candidate'].pop()
        if name == 'changed saved glyph': positive['saved'][cell]['x'] += 1
        if name == 'introduced default margin': positive['savedMargins']['table']['left'] = ('0', 'dxa')
        if name == 'changed saved margin': explicit['savedMargins']['table']['left'] = ('1', 'dxa')
        if name == 'changed override owner': altered['partial-mixed']['savedMargins']['cells'].reverse()
        if name == 'changed invalid metadata case': altered['invalid-metadata']['candidate'][0]['x'] += 1
        try:
            check(altered)
        except (ValueError, KeyError, TypeError, IndexError) as error:
            negatives[name] = str(error)
        else:
            raise ValueError('Negative control unexpectedly passed: ' + name)
    print(json.dumps({'glyphOriginTolerancePt': TOLERANCE, 'cases': result, 'negativeControls': negatives,
                      'controlMethod': 'Parsed glyph observations changed in memory.'}, indent=2))


if __name__ == '__main__':
    main()
