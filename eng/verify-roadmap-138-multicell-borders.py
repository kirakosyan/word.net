#!/usr/bin/env python3
"""Check small-grid glyphs, shared strokes and independently saved OOXML."""
import argparse
import copy
import importlib.util
import json
from pathlib import Path

import fitz

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('border_verifier', ROOT / 'eng/verify-roadmap-138-border-geometry.py')
border = importlib.util.module_from_spec(spec)
spec.loader.exec_module(border)
plain = border.plain
SHAPES = ('single', 'rows', 'columns', 'grid')
POSITIVE = (tuple('nil-' + shape + '-' + margin for shape in SHAPES for margin in ('implicit', 'zero'))
            + tuple('implicit-%d-%s' % (size, shape) for shape in SHAPES for size in (4, 8, 12, 16, 20, 24))
            + tuple('zero-%d-%s' % (size, shape) for shape in SHAPES for size in (2, 6, 8, 24))
            + ('grid-top', 'grid-override'))
CONTROLS = ('implicit-2-rows', 'zero-3-rows', 'three-rows', 'three-columns', 'mixed-borders', 'horizontal-only')
DIAGNOSED = ('long-wrap', 'multiple-pages', 'section-columns')
TOLERANCE = 0.25


def pdf(path):
    glyphs, strokes = [], []
    with fitz.open(path) as document:
        for page_number, page in enumerate(document):
            for block in page.get_text('rawdict')['blocks']:
                for line in block.get('lines', []):
                    for span in line['spans']:
                        for char in span['chars']:
                            glyphs.append({'text': char['c'], 'font': span['font'], 'x': char['origin'][0], 'y': char['origin'][1], 'page': page_number})
            for item in page.get_drawings():
                x0, y0, x1, y1 = item['rect']
                if abs(y1 - y0) < .001:
                    strokes.append({'page': page_number, 'axis': 'h', 'coordinate': y0, 'start': x0, 'end': x1, 'width': item['width'], 'color': item['color']})
                elif abs(x1 - x0) < .001:
                    strokes.append({'page': page_number, 'axis': 'v', 'coordinate': x0, 'start': y0, 'end': y1, 'width': item['width'], 'color': item['color']})
                else:
                    raise ValueError('Expected straight table strokes')
    return glyphs, normalize(strokes)


def normalize(strokes):
    result = []
    for stroke in sorted(strokes, key=lambda s: (s['page'], s['axis'], s['coordinate'], s['start'])):
        if result:
            last = result[-1]
            if (last['page'] == stroke['page'] and last['axis'] == stroke['axis'] and abs(last['coordinate'] - stroke['coordinate']) < .002
                    and abs(last['width'] - stroke['width']) < .002 and last['color'] == stroke['color'] and stroke['start'] <= last['end'] + .002):
                last['end'] = max(last['end'], stroke['end'])
                continue
        result.append(dict(stroke))
    return result


def same_glyphs(left, right, tolerance, message):
    if len(left) != len(right):
        raise ValueError(message + ': character count')
    for a, b in zip(left, right):
        if a['text'] != b['text'] or a['font'] != b['font'] or a['page'] != b['page'] or any(abs(a[axis] - b[axis]) > tolerance for axis in ('x', 'y')):
            raise ValueError(message + ': glyph or origin')


def same_strokes(left, right, tolerance, message):
    if len(left) != len(right):
        raise ValueError(message + ': stroke count')
    for a, b in zip(left, right):
        if (a['page'] != b['page'] or a['axis'] != b['axis'] or a['color'] != b['color'] or abs(a['width'] - b['width']) > .005
                or any(abs(a[key] - b[key]) > tolerance for key in ('coordinate', 'start', 'end'))):
            raise ValueError(message + ': stroke geometry or color')


def check(values):
    output = {}
    for name in POSITIVE + CONTROLS + DIAGNOSED:
        item = values[name]; roles = item['roles']; parent, candidate, office = (item[key] for key in ('parent', 'candidate', 'office'))
        if not roles or any(len(glyphs) != len(roles) for glyphs in (parent, candidate, office)):
            raise ValueError('Source and output character counts differ: ' + name)
        for (text, role), *glyphs in zip(roles, parent, candidate, office):
            if role not in ('before', 'cell-default', 'cell-specified', 'after') or any(g['text'] != text or g['font'] != 'NotoSans-Regular' for g in glyphs):
                raise ValueError('Source text, reading order or pinned font differs: ' + name)
        delta = max(abs(a[axis] - b[axis]) for a, b in zip(candidate, office) for axis in ('x', 'y'))
        if name in POSITIVE:
            same_glyphs(candidate, office, TOLERANCE, 'Office comparison ' + name)
            same_strokes(item['candidateStrokes'], item['officeStrokes'], TOLERANCE, 'Office comparison ' + name)
            same_glyphs(candidate, item['saved'], .002, 'Saved output ' + name)
            same_strokes(item['candidateStrokes'], item['savedStrokes'], .002, 'Saved output ' + name)
            if item['sourceMargins'] != item['savedMargins'] or item['sourceBorders'] != item['savedBorders']:
                raise ValueError('Saved border or margin ownership changed: ' + name)
            if '-single' in name:
                same_glyphs(parent, candidate, .002, 'Previously qualified single cell changed ' + name)
                same_strokes(item['parentStrokes'], item['candidateStrokes'], .002, 'Previously qualified single cell changed ' + name)
        elif name in CONTROLS:
            same_glyphs(parent, candidate, .002, 'Unqualified case changed ' + name)
            same_strokes(item['parentStrokes'], item['candidateStrokes'], .002, 'Unqualified case changed ' + name)
            if delta <= TOLERANCE:
                raise ValueError('Expected the unqualified reference difference to remain visible: ' + name)
        elif 'Table.BorderGeometry' not in item['diagnostics']:
            raise ValueError('Unqualified multiline or multipage geometry was not diagnosed: ' + name)
        output[name] = {'characters': len(roles), 'officeMaximumGlyphOriginDeltaPt': delta,
                        'officeGlyphComparisonPassed': delta <= TOLERANCE,
                        'normalizedStrokes': len(item['candidateStrokes']), 'savedGeometryMatches': name in POSITIVE}
    return output


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('sources', 'parent', 'candidate', 'office', 'saved'):
        parser.add_argument(name, type=Path)
    args = parser.parse_args(); observations = {}
    for name in POSITIVE + CONTROLS + DIAGNOSED:
        source, parent = args.sources / (name + '.docx'), args.parent / name / 'output.pdf'
        candidate, office = args.candidate / name / 'output.pdf', args.office / (name + '.pdf')
        parent_glyphs, parent_strokes = pdf(parent); candidate_glyphs, candidate_strokes = pdf(candidate); office_glyphs, office_strokes = pdf(office)
        item = {'roles': plain.source_roles(source), 'parent': parent_glyphs, 'candidate': candidate_glyphs, 'office': office_glyphs,
                'parentStrokes': parent_strokes, 'candidateStrokes': candidate_strokes, 'officeStrokes': office_strokes,
                'diagnostics': json.loads((args.candidate / name / 'diagnostics.json').read_text())}
        if name in POSITIVE:
            saved_docx, saved_pdf = args.saved / name / 'saved.docx', args.saved / name / 'saved.pdf'
            item['saved'], item['savedStrokes'] = pdf(saved_pdf)
            item.update({'sourceMargins': plain.source_margins(source), 'savedMargins': plain.source_margins(saved_docx),
                         'sourceBorders': border.borders(source), 'savedBorders': border.borders(saved_docx)})
        observations[name] = item
    result = check(observations); controls = {}
    for name in ('wrong character', 'wrong font', 'shifted first cell', 'shifted second row', 'shifted following text', 'shifted office',
                 'changed outer stroke', 'changed shared horizontal', 'missing shared vertical', 'changed stroke width', 'changed stroke color',
                 'changed saved glyph', 'changed saved stroke', 'changed border', 'changed margin owner', 'changed single cell',
                 'changed unqualified case', 'missing multiline diagnostic', 'missing page diagnostic', 'missing column diagnostic'):
        changed = copy.deepcopy(observations); item = changed['implicit-8-grid']
        cell = next(i for i, role in enumerate(item['roles']) if role[0] == 'M')
        second = next(i for i, role in enumerate(item['roles']) if role[0] == 'P')
        after = next(i for i, role in enumerate(item['roles']) if role[1] == 'after')
        horizontal = next(s for s in item['candidateStrokes'] if s['axis'] == 'h')
        shared = [s for s in item['candidateStrokes'] if s['axis'] == 'h'][1]
        vertical = [s for s in item['candidateStrokes'] if s['axis'] == 'v'][1]
        if name == 'wrong character': item['candidate'][cell]['text'] = '?'
        if name == 'wrong font': item['candidate'][cell]['font'] = 'other'
        if name == 'shifted first cell': item['candidate'][cell]['x'] += 1
        if name == 'shifted second row': item['candidate'][second]['y'] += 1
        if name == 'shifted following text': item['candidate'][after]['y'] += 1
        if name == 'shifted office': item['office'][cell]['x'] += 2
        if name == 'changed outer stroke': horizontal['coordinate'] += 1
        if name == 'changed shared horizontal': shared['coordinate'] += 1
        if name == 'missing shared vertical': item['candidateStrokes'].remove(vertical)
        if name == 'changed stroke width': horizontal['width'] += 1
        if name == 'changed stroke color': horizontal['color'] = (1, 0, 0)
        if name == 'changed saved glyph': item['saved'][cell]['x'] += 1
        if name == 'changed saved stroke': item['savedStrokes'][0]['coordinate'] += 1
        if name == 'changed border': item['savedBorders']['top'] = ('single', ('16', '000000'))
        if name == 'changed margin owner': changed['grid-override']['savedMargins']['cells'][1] = None
        if name == 'changed single cell': changed['implicit-8-single']['candidate'][0]['x'] += .01
        if name == 'changed unqualified case': changed['three-rows']['candidate'][0]['x'] += 1
        if name == 'missing multiline diagnostic': changed['long-wrap']['diagnostics'] = []
        if name == 'missing page diagnostic': changed['multiple-pages']['diagnostics'] = []
        if name == 'missing column diagnostic': changed['section-columns']['diagnostics'] = []
        try:
            check(changed)
        except (ValueError, KeyError, TypeError, IndexError) as error:
            controls[name] = str(error)
        else:
            raise ValueError('Negative control unexpectedly passed: ' + name)
    print(json.dumps({'glyphAndStrokeTolerancePt': TOLERANCE, 'cases': result, 'negativeControls': controls,
                      'controlMethod': 'Parsed glyphs, normalized stroke segments and OOXML observations changed in memory.'}, indent=2))


if __name__ == '__main__':
    main()
