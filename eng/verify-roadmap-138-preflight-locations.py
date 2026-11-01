#!/usr/bin/env python3
"""Compare privacy-safe numeric field positions with independently parsed OOXML."""
import argparse
import copy
import json
from pathlib import Path
import re
import xml.etree.ElementTree as ET
from zipfile import ZipFile

W = '{http://schemas.openxmlformats.org/wordprocessingml/2006/main}'


def read(source, output):
    with ZipFile(source) as package:
        root = ET.fromstring(package.read('word/document.xml'))
    positions = {}
    def visit(element, indices):
        if element.tag == W + 'fldSimple':
            instruction = element.get(W + 'instr', '')
            if not instruction.startswith('MERGEFIELD '):
                raise ValueError('Unexpected simple field instruction')
            path = instruction[len('MERGEFIELD '):]
            if path in positions:
                raise ValueError('Duplicate synthetic field path')
            positions[path] = indices
        for index, child in enumerate(element, 1):
            visit(child, indices + [index])
    visit(root, [1])
    complex_instructions = [' '.join((node.text or '').split()) for node in root.iter(W + 'instrText')]
    return {'positions': positions, 'complexInstructions': complex_instructions,
            'default': json.loads((output / 'default.json').read_text(encoding='utf-8')),
            'findings': json.loads((output / 'findings.json').read_text(encoding='utf-8'))}


def check(simple, complex_value):
    if set(simple['positions']) != {'Customer.Name', 'Customer.Code'} or simple['complexInstructions']:
        raise ValueError('Unexpected simple source fields')
    if complex_value['positions'] or complex_value['complexInstructions'] != ['MERGEFIELD Secret']:
        raise ValueError('Unexpected complex source field')
    missing = [finding for finding in simple['findings'] if finding['Code'] == 'Plan.MissingPath']
    if len(missing) != 2 or {finding['path'] for finding in missing} != set(simple['positions']):
        raise ValueError('Missing-path findings differ from source fields')
    aliases = set()
    for finding in missing:
        if finding['indices'] != simple['positions'][finding['path']] or finding['Unknown']:
            raise ValueError('Numeric source position does not identify the field')
        if not re.fullmatch(r'part-[0-9]{4}', finding['PartId'] or ''):
            raise ValueError('Part alias is absent or not opaque')
        aliases.add(finding['PartId'])
    if len(aliases) != 1:
        raise ValueError('Fields in one part received different aliases')
    default_missing = [item for item in simple['default']['Findings'] if item['Code'] == 'Plan.MissingPath']
    if sorted(item['SourceElementIndices'] for item in default_missing) != sorted(simple['positions'].values()):
        raise ValueError('Default report positions differ from the inspected findings')
    complex_missing = [finding for finding in complex_value['findings'] if finding['Code'] == 'Plan.MissingPath']
    if len(complex_missing) != 1 or complex_missing[0]['path'] != 'Secret' or complex_missing[0]['indices'] is not None:
        raise ValueError('Complex field was omitted or assigned invented precision')
    for value in (simple, complex_value):
        serialized = json.dumps(value['default'], sort_keys=True)
        if any(secret in serialized for secret in ('Customer', 'Secret', 'word/document.xml', 'fldSimple', 'instrText')):
            raise ValueError('Default report disclosed source names or element names')
        if value['default']['IsReusable'] or not any(item['Code'] == 'Plan.UninspectedCoverage' and item['Unknown'] for item in value['default']['Findings']):
            raise ValueError('Uninspected coverage or reuse boundary changed')
    return {'simpleFieldPositions': len(missing), 'complexPositionUnavailable': True,
            'defaultNamesDisclosed': False, 'positions': simple['positions']}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('sources', type=Path)
    parser.add_argument('outputs', type=Path)
    args = parser.parse_args()
    values = {name: read(args.sources / (name + '.docx'), args.outputs / name) for name in ('simple', 'complex')}
    result = check(values['simple'], values['complex'])
    negatives = {}
    for name in ('changed source field', 'wrong body index', 'wrong cell index', 'missing simple position',
                 'invented complex position', 'leaked field name', 'leaked part name', 'lost unknown coverage', 'changed default position'):
        simple, complex_value = copy.deepcopy(values['simple']), copy.deepcopy(values['complex'])
        missing = [finding for finding in simple['findings'] if finding['Code'] == 'Plan.MissingPath']
        if name == 'changed source field': simple['positions']['Other'] = simple['positions'].pop('Customer.Code')
        if name == 'wrong body index': missing[0]['indices'][-1] += 1
        if name == 'wrong cell index': missing[1]['indices'][-1] += 1
        if name == 'missing simple position': missing[0]['indices'] = None
        if name == 'invented complex position': next(f for f in complex_value['findings'] if f['Code'] == 'Plan.MissingPath')['indices'] = [1]
        if name == 'leaked field name': simple['default']['extra'] = 'Customer.Name'
        if name == 'leaked part name': simple['default']['extra'] = 'word/document.xml'
        if name == 'lost unknown coverage': simple['default']['Findings'] = [f for f in simple['default']['Findings'] if f['Code'] != 'Plan.UninspectedCoverage']
        if name == 'changed default position': next(f for f in simple['default']['Findings'] if f['Code'] == 'Plan.MissingPath')['SourceElementIndices'] = [1]
        try:
            check(simple, complex_value)
        except (ValueError, KeyError, TypeError, IndexError) as error:
            negatives[name] = str(error)
        else:
            raise ValueError('Negative control unexpectedly passed: ' + name)
    print(json.dumps({'result': result, 'negativeControls': negatives,
                      'controlMethod': 'Parsed source/output observations changed in memory.'}, indent=2))


if __name__ == '__main__':
    main()
