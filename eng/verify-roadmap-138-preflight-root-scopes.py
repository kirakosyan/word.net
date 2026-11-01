#!/usr/bin/env python3
"""Compare root-scope findings with independent XML positions and merge output."""
import argparse
import copy
import json
from pathlib import Path
import re
import xml.etree.ElementTree as ET
from zipfile import ZipFile

W = '{http://schemas.openxmlformats.org/wordprocessingml/2006/main}'


def read(name, sources, outputs):
    with ZipFile(sources / (name + '.docx')) as package:
        root = ET.fromstring(package.read('word/document.xml'))
    positions = {}
    def visit(element, indices):
        if element.tag == W + 'fldSimple':
            instruction = element.get(W + 'instr', '')
            if instruction.startswith('MERGEFIELD '):
                positions[instruction[len('MERGEFIELD '):]] = indices
        if element.tag == W + 'sdt':
            tag = element.find(W + 'sdtPr/' + W + 'tag')
            if tag is not None and tag.get(W + 'val', '').startswith('merge:'):
                positions[tag.get(W + 'val')[len('merge:'):]] = indices
        for index, child in enumerate(element, 1):
            visit(child, indices + [index])
    visit(root, [1])
    directory = outputs / name
    return {'positions': positions, 'default': json.loads((directory / 'default.json').read_text(encoding='utf-8')),
            'result': json.loads((directory / 'result.json').read_text(encoding='utf-8'))}


def check(values, output_text):
    root, wrapped, table = (values[key] for key in ('root', 'wrapped', 'repeat_table'))
    expected = {'Outside', 'Cell', 'Body'}
    findings = [f for f in root['result']['findings'] if f['Code'] == 'Plan.MissingPath']
    if {f['path'] for f in findings} != expected or len(findings) != len(expected):
        raise ValueError('Root-owned missing paths differ')
    for finding in findings:
        if finding['indices'] != root['positions'][finding['path']] or not re.fullmatch(r'part-[0-9]{4}', finding['PartId'] or ''):
            raise ValueError('Root finding position or part alias differs')
    requirements = root['result']['requirements']
    if {r['path'] for r in requirements} != expected or any(r['ScopeUnknown'] for r in requirements):
        raise ValueError('Root requirements or scope differ')
    if root['result']['error'] != 'Template.MissingValue' or root['result']['before'] != root['result']['afterFailure']:
        raise ValueError('Failed merge changed the source or used a different error')
    if root['result']['afterMerge'] != ['inner', 'outer', 'cell', 'body'] or output_text != ['inner', 'outer', 'cell', 'body']:
        raise ValueError('Successful merge or independently read DOCX differs')
    wrapped_requirements = {r['path']: r for r in wrapped['result']['requirements']}
    if 'Wrapped' not in wrapped_requirements or not wrapped_requirements['Wrapped']['ScopeUnknown']:
        raise ValueError('Flattened wrapper scope was treated as known')
    if any(f['Code'] == 'Plan.MissingPath' and f['path'] == 'Wrapped' for f in wrapped['result']['findings']):
        raise ValueError('Flattened wrapper received a definite missing-path finding')
    table_requirements = {r['path']: r for r in table['result']['requirements']}
    if set(table_requirements) != {'Outside'} or table_requirements['Outside']['ScopeUnknown']:
        raise ValueError('A repeated table field was confused with the independent root field')
    for value in values.values():
        serialized = json.dumps(value['default'], sort_keys=True)
        if any(json.dumps(secret) in serialized for secret in ('Inside', 'Outside', 'Cell', 'Body', 'Items', 'Wrapped', 'Hidden', 'word/document.xml')):
            raise ValueError('Default report disclosed a source or path name')
        if value['result']['IsReusable'] or not any(f['Code'] == 'Plan.RepeatScope' and f['Unknown'] for f in value['result']['findings']):
            raise ValueError('Repeat uncertainty or reuse boundary changed')
    if not any(f['Code'] == 'Plan.OwnerCoverage' and f['Unknown'] for f in root['result']['findings']):
        raise ValueError('Whole-document owner coverage was treated as complete')
    return {'rootPaths': sorted(expected), 'rootPositionsMatchXml': True, 'failedMergeUnchanged': True,
            'independentOutputMatches': True, 'wrappedAndRepeatedContentsRemainUnknown': True, 'defaultNamesDisclosed': False}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('sources', type=Path)
    parser.add_argument('outputs', type=Path)
    args = parser.parse_args()
    values = {name: read(name, args.sources, args.outputs) for name in ('root', 'wrapped', 'repeat_table')}
    with ZipFile(args.outputs / 'root/merged.docx') as package:
        document = ET.fromstring(package.read('word/document.xml'))
    output_text = [node.text or '' for node in document.iter(W + 't')]
    result = check(values, output_text)
    negatives = {}
    for name in ('wrong path', 'wrong position', 'wrong root scope', 'changed failed merge', 'changed output',
                 'wrapper marked known', 'wrapper false missing', 'repeat table leaked', 'lost repeat uncertainty',
                 'lost owner uncertainty', 'reusable report', 'leaked name'):
        altered, text = copy.deepcopy(values), list(output_text)
        root, wrapped, table = (altered[key]['result'] for key in ('root', 'wrapped', 'repeat_table'))
        if name == 'wrong path': next(f for f in root['findings'] if f['Code'] == 'Plan.MissingPath')['path'] = 'Other'
        if name == 'wrong position': next(f for f in root['findings'] if f['Code'] == 'Plan.MissingPath')['indices'][-1] += 1
        if name == 'wrong root scope': root['requirements'][0]['ScopeUnknown'] = True
        if name == 'changed failed merge': root['afterFailure'] = ['changed']
        if name == 'changed output': text[0] = 'changed'
        if name == 'wrapper marked known': next(r for r in wrapped['requirements'] if r['path'] == 'Wrapped')['ScopeUnknown'] = False
        if name == 'wrapper false missing': wrapped['findings'].append({'Code': 'Plan.MissingPath', 'path': 'Wrapped'})
        if name == 'repeat table leaked': table['requirements'].append({'path': 'Hidden', 'ScopeUnknown': False})
        if name == 'lost repeat uncertainty': root['findings'] = [f for f in root['findings'] if f['Code'] != 'Plan.RepeatScope']
        if name == 'lost owner uncertainty': root['findings'] = [f for f in root['findings'] if f['Code'] != 'Plan.OwnerCoverage']
        if name == 'reusable report': root['IsReusable'] = True
        if name == 'leaked name': altered['root']['default']['extra'] = 'Outside'
        try:
            check(altered, text)
        except (ValueError, KeyError, TypeError, IndexError) as error:
            negatives[name] = str(error)
        else:
            raise ValueError('Negative control unexpectedly passed: ' + name)
    print(json.dumps({'result': result, 'negativeControls': negatives,
                      'controlMethod': 'Parsed source/output observations changed in memory.'}, indent=2))


if __name__ == '__main__':
    main()
