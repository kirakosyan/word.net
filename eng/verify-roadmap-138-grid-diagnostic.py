#!/usr/bin/env python3
"""Check oversized uniform-grid warnings, strict atomicity and prior geometry."""
import argparse
import copy
import importlib.util
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('small_grid', ROOT / 'eng/verify-roadmap-138-multicell-borders.py')
small = importlib.util.module_from_spec(spec)
spec.loader.exec_module(small)
SUPPORTED = ('3x1-nil-implicit', '1x3-8-zero', '3x3-16-implicit', '4x4-nil-zero',
             '4x4-8-implicit', '4x4-24-zero', '2x4-8-implicit')
OUTSIDE = ('outside-5x1', 'outside-1x5', 'outside-5x5')
CODE = 'Table.BorderGeometry'
LOCATION = {'part': 'word/document.xml', 'path': '/document[1]/body[1]/tbl[1]', 'phase': 'Layout'}


def check(observations):
    for name in SUPPORTED + OUTSIDE:
        item = observations[name]
        small.same_glyphs(item['parent'], item['candidate'], .002, 'Changed lenient glyphs ' + name)
        small.same_strokes(item['parentStrokes'], item['candidateStrokes'], .002, 'Changed lenient strokes ' + name)
        data = item['result']
        if not data['inputOpen'] or not data['strict']['inputOpen'] or not data['strict']['outputOpen']:
            raise ValueError('Caller stream closed: ' + name)
        warnings = [d for d in data['diagnostics'] if d['Code'] == CODE]
        if name in SUPPORTED:
            if warnings or data['strict']['error'] is not None or data['clonedError'] is not None or data['savedStrict']['error'] is not None:
                raise ValueError('Qualified geometry was rejected: ' + name)
            continue
        if (len(warnings) != 1 or warnings[0]['severity'] != 'Warning' or warnings[0]['Count'] != 1
                or warnings[0]['LocationsTruncated'] or warnings[0]['locations'] != [LOCATION]):
            raise ValueError('Missing or imprecise diagnostic: ' + name)
        if (CODE not in (data['strict']['error'] or '') or not data['strict']['outputUntouched']
                or CODE not in (data['clonedError'] or '') or not data['clonedUntouched']
                or CODE not in (data['savedStrict']['error'] or '') or not data['savedStrict']['outputUntouched']
                or not data['savedStrict']['inputOpen'] or not data['savedStrict']['outputOpen']):
            raise ValueError('Strict rejection or output atomicity changed: ' + name)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('parent', type=Path)
    parser.add_argument('candidate', type=Path)
    args = parser.parse_args()
    observations = {}
    for name in SUPPORTED + OUTSIDE:
        parent, parent_strokes = small.pdf(args.parent / name / 'output.pdf')
        candidate, candidate_strokes = small.pdf(args.candidate / name / 'output.pdf')
        observations[name] = {'parent': parent, 'parentStrokes': parent_strokes, 'candidate': candidate,
                              'candidateStrokes': candidate_strokes,
                              'result': json.loads((args.candidate / name / 'result.json').read_text())}
    check(observations)
    controls = {}
    for name in ('missing diagnostic', 'false positive', 'wrong severity', 'wrong count', 'truncated location',
                 'wrong part', 'wrong path', 'wrong phase', 'strict accepted', 'strict changed output',
                 'stream closed', 'clone accepted', 'clone changed output', 'saved accepted', 'saved changed output',
                 'shifted glyph', 'changed stroke'):
        changed = copy.deepcopy(observations)
        item = changed['outside-5x5']
        data = item['result']
        warning = next(d for d in data['diagnostics'] if d['Code'] == CODE)
        if name == 'missing diagnostic': data['diagnostics'] = []
        if name == 'false positive': changed['4x4-8-implicit']['result']['diagnostics'].append(copy.deepcopy(warning))
        if name == 'wrong severity': warning['severity'] = 'Info'
        if name == 'wrong count': warning['Count'] = 2
        if name == 'truncated location': warning['LocationsTruncated'] = True
        if name == 'wrong part': warning['locations'][0]['part'] = 'word/header1.xml'
        if name == 'wrong path': warning['locations'][0]['path'] = '/document[1]/body[1]/tbl[2]'
        if name == 'wrong phase': warning['locations'][0]['phase'] = 'Write'
        if name == 'strict accepted': data['strict']['error'] = None
        if name == 'strict changed output': data['strict']['outputUntouched'] = False
        if name == 'stream closed': data['strict']['inputOpen'] = False
        if name == 'clone accepted': data['clonedError'] = None
        if name == 'clone changed output': data['clonedUntouched'] = False
        if name == 'saved accepted': data['savedStrict']['error'] = None
        if name == 'saved changed output': data['savedStrict']['outputUntouched'] = False
        if name == 'shifted glyph': item['candidate'][0]['x'] += .01
        if name == 'changed stroke': item['candidateStrokes'][0]['coordinate'] += .01
        try:
            check(changed)
        except (ValueError, KeyError, TypeError, IndexError) as error:
            controls[name] = str(error)
        else:
            raise ValueError('Negative control unexpectedly passed: ' + name)
    print(json.dumps({'supported': len(SUPPORTED), 'outside': len(OUTSIDE), 'negativeControls': controls,
                      'controlMethod': 'Parsed diagnostic, output-atomicity and vector observations changed in memory.'}, indent=2))


if __name__ == '__main__':
    main()
