#!/usr/bin/env python3
"""Check larger-grid glyphs, shared strokes, saved ownership and boundaries."""
import argparse
import copy
import importlib.util
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('small_grid', ROOT / 'eng/verify-roadmap-138-multicell-borders.py')
small = importlib.util.module_from_spec(spec)
spec.loader.exec_module(small)
SHAPES = ((3, 1), (1, 3), (3, 3), (4, 1), (1, 4), (4, 4), (2, 4), (4, 2))
POSITIVE = tuple('%dx%d-%s-%s' % (rows, columns, size, margin) for rows, columns in SHAPES
                 for size in ('nil', '4', '8', '16', '24') for margin in ('implicit', 'zero'))
CONTROLS = ('outside-5x1', 'outside-1x5', 'outside-5x5')


def check(observations):
    result = {}
    for name in POSITIVE + CONTROLS:
        item = observations[name]
        roles = item['roles']
        parent, candidate, office = (item[key] for key in ('parent', 'candidate', 'office'))
        if not roles or any(len(glyphs) != len(roles) for glyphs in (parent, candidate, office)):
            raise ValueError('Character count differs: ' + name)
        for (text, role), *glyphs in zip(roles, parent, candidate, office):
            if role not in ('before', 'cell-default', 'cell-specified', 'after') or any(g['text'] != text or g['font'] != 'NotoSans-Regular' for g in glyphs):
                raise ValueError('Source text, reading order or pinned font differs: ' + name)
        delta = max(abs(a[axis] - b[axis]) for a, b in zip(candidate, office) for axis in ('x', 'y'))
        if name in POSITIVE:
            small.same_glyphs(candidate, office, .25, 'Office comparison ' + name)
            small.same_strokes(item['candidateStrokes'], item['officeStrokes'], .25, 'Office comparison ' + name)
            small.same_glyphs(candidate, item['saved'], .002, 'Saved output ' + name)
            small.same_strokes(item['candidateStrokes'], item['savedStrokes'], .002, 'Saved output ' + name)
            if item['sourceMargins'] != item['savedMargins'] or item['sourceBorders'] != item['savedBorders']:
                raise ValueError('Saved border or margin ownership changed: ' + name)
        else:
            small.same_glyphs(parent, candidate, .002, 'Unqualified case changed ' + name)
            small.same_strokes(item['parentStrokes'], item['candidateStrokes'], .002, 'Unqualified case changed ' + name)
            if delta <= .25:
                raise ValueError('Expected an unqualified reference difference: ' + name)
        result[name] = {'characters': len(roles), 'officeMaximumGlyphOriginDeltaPt': delta,
                        'officeGlyphComparisonPassed': delta <= .25, 'normalizedStrokes': len(item['candidateStrokes']),
                        'savedGeometryMatches': name in POSITIVE}
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('sources', 'parent', 'candidate', 'office', 'saved'):
        parser.add_argument(name, type=Path)
    args = parser.parse_args()
    observations = {}
    for name in POSITIVE + CONTROLS:
        source = args.sources / (name + '.docx')
        parent, parent_strokes = small.pdf(args.parent / name / 'output.pdf')
        candidate, candidate_strokes = small.pdf(args.candidate / name / 'output.pdf')
        office, office_strokes = small.pdf(args.office / (name + '.pdf'))
        item = {'roles': small.plain.source_roles(source), 'parent': parent, 'candidate': candidate, 'office': office,
                'parentStrokes': parent_strokes, 'candidateStrokes': candidate_strokes, 'officeStrokes': office_strokes}
        if name in POSITIVE:
            saved = args.saved / name / 'saved.docx'
            item['saved'], item['savedStrokes'] = small.pdf(args.saved / name / 'saved.pdf')
            item.update({'sourceMargins': small.plain.source_margins(source), 'savedMargins': small.plain.source_margins(saved),
                         'sourceBorders': small.border.borders(source), 'savedBorders': small.border.borders(saved)})
        observations[name] = item
    result = check(observations)
    controls = {}
    for name in ('wrong character', 'wrong font', 'shifted first cell', 'shifted last row', 'shifted following text',
                 'shifted office', 'changed outer stroke', 'changed shared horizontal', 'missing shared vertical',
                 'changed stroke width', 'changed stroke color', 'changed saved glyph', 'changed saved stroke',
                 'changed border', 'changed margin owner', 'changed unqualified case'):
        changed = copy.deepcopy(observations)
        item = changed['4x4-8-implicit']
        cell = next(i for i, role in enumerate(item['roles']) if role[1].startswith('cell'))
        last = max(i for i, role in enumerate(item['roles']) if role[1].startswith('cell'))
        after = next(i for i, role in enumerate(item['roles']) if role[1] == 'after')
        horizontal = next(s for s in item['candidateStrokes'] if s['axis'] == 'h')
        shared = [s for s in item['candidateStrokes'] if s['axis'] == 'h'][1]
        vertical = [s for s in item['candidateStrokes'] if s['axis'] == 'v'][1]
        if name == 'wrong character': item['candidate'][cell]['text'] = '?'
        if name == 'wrong font': item['candidate'][cell]['font'] = 'other'
        if name == 'shifted first cell': item['candidate'][cell]['x'] += 1
        if name == 'shifted last row': item['candidate'][last]['y'] += 1
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
        if name == 'changed margin owner': item['savedMargins']['table'] = 'changed'
        if name == 'changed unqualified case': changed['outside-5x5']['candidate'][0]['x'] += 1
        try:
            check(changed)
        except (ValueError, KeyError, TypeError, IndexError) as error:
            controls[name] = str(error)
        else:
            raise ValueError('Negative control unexpectedly passed: ' + name)
    print(json.dumps({'glyphAndStrokeTolerancePt': .25, 'cases': result, 'negativeControls': controls,
                      'controlMethod': 'Parsed glyphs, normalized stroke segments and OOXML observations changed in memory.'}, indent=2))


if __name__ == '__main__':
    main()
