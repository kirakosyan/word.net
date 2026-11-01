#!/usr/bin/env python3
"""Compare root-repeat predictions with source XML and actual merge outcomes."""
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
        if element.tag == W + 'sdt':
            tag = element.find(W + 'sdtPr/' + W + 'tag')
            if tag is not None:
                positions[tag.get(W + 'val')] = indices
        for index, child in enumerate(element, 1):
            visit(child, indices + [index])
    visit(root, [1])
    directory = outputs / name
    return {'positions': positions, 'default': json.loads((directory / 'default.json').read_text(encoding='utf-8')),
            'result': json.loads((directory / 'result.json').read_text(encoding='utf-8'))}


def check(values, output_text):
    simple, mixed, complex_value = (values[key] for key in ('simple', 'mixed', 'complex'))
    if set(simple['positions']) != {'repeat:Items'}:
        raise ValueError('Unexpected root-repeat source')
    missing = [f for f in simple['result']['findings'] if f['Code'] == 'Plan.MissingPath']
    if len(missing) != 1 or missing[0]['path'] != 'Items' or missing[0]['indices'] != simple['positions']['repeat:Items']:
        raise ValueError('Missing repeat path or location differs from source XML')
    if not re.fullmatch(r'part-[0-9]{4}', missing[0]['PartId'] or ''):
        raise ValueError('Repeat part alias is absent or not opaque')
    requirements = simple['result']['requirements']
    if len(requirements) != 1 or requirements[0]['path'] != 'Items' or requirements[0]['ScopeUnknown'] or requirements[0]['Count'] != 1:
        raise ValueError('Root collection requirement differs')
    if not simple['result']['wrongTypeFinding'] or simple['result']['validScopeUnknown'] or simple['result']['validType'] != 5:
        raise ValueError('Repeat collection type or scope differs')
    if simple['result']['missingError'] != 'Template.MissingValue' or simple['result']['typeError'] != 'Template.Collection':
        raise ValueError('Repeat path/type predictions differ from failed merges')
    if simple['result']['before'] != simple['result']['afterFailures']:
        raise ValueError('Failed merges changed the document')
    if simple['result']['changes'] != 1 or simple['result']['afterMerge'] != ['cached', 'cached'] or output_text != ['cached', 'cached']:
        raise ValueError('Repeated output differs from the independent package read')
    mixed_requirements = mixed['result']['requirements']
    if len(mixed_requirements) != 1 or mixed_requirements[0]['Count'] != 2 or not mixed_requirements[0]['ScopeUnknown'] or mixed_requirements[0]['DeclaredType'] is not None:
        raise ValueError('Mixed-scope requirement is not explicitly uncertain')
    if complex_value['result']['requirements'] or any(f['Code'] == 'Plan.MissingPath' for f in complex_value['result']['findings']):
        raise ValueError('Complex repeat was incorrectly treated as inspected')
    for value in values.values():
        serialized = json.dumps(value['default'], sort_keys=True)
        if any(secret in serialized for secret in ('Items', 'Other', 'word/document.xml', 'sdtContent')):
            raise ValueError('Default report disclosed a source or path name')
        if value['result']['IsReusable'] or not any(f['Code'] == 'Plan.RepeatScope' and f['Unknown'] for f in value['result']['findings']):
            raise ValueError('Repeat coverage or reuse boundary changed')
    return {'rootPathLocated': True, 'missingAndTypeFailuresMatch': True, 'failedMergesUnchanged': True,
            'successfulRepeatedParagraphs': 2, 'mixedAndComplexScopesUnknown': True, 'defaultNamesDisclosed': False}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('sources', type=Path)
    parser.add_argument('outputs', type=Path)
    args = parser.parse_args()
    values = {name: read(name, args.sources, args.outputs) for name in ('simple', 'mixed', 'complex')}
    with ZipFile(args.outputs / 'simple/merged.docx') as package:
        root = ET.fromstring(package.read('word/document.xml'))
    output_text = [node.text or '' for node in root.iter(W + 't')]
    result = check(values, output_text)
    negatives = {}
    for name in ('wrong source position', 'wrong root path', 'wrong collection type', 'changed failed merge', 'changed output',
                 'leaked path', 'leaked part', 'lost repeat uncertainty', 'mixed scope claimed', 'invented complex requirement'):
        altered, text = copy.deepcopy(values), list(output_text)
        simple = altered['simple']['result']
        if name == 'wrong source position': next(f for f in simple['findings'] if f['Code'] == 'Plan.MissingPath')['indices'][-1] += 1
        if name == 'wrong root path': next(f for f in simple['findings'] if f['Code'] == 'Plan.MissingPath')['path'] = 'Other'
        if name == 'wrong collection type': simple['validType'] = 0
        if name == 'changed failed merge': simple['afterFailures'] = ['changed']
        if name == 'changed output': text[0] = 'changed'
        if name == 'leaked path': altered['simple']['default']['extra'] = 'Items'
        if name == 'leaked part': altered['simple']['default']['extra'] = 'word/document.xml'
        if name == 'lost repeat uncertainty': simple['findings'] = [f for f in simple['findings'] if f['Code'] != 'Plan.RepeatScope']
        if name == 'mixed scope claimed': altered['mixed']['result']['requirements'][0]['ScopeUnknown'] = False
        if name == 'invented complex requirement': altered['complex']['result']['requirements'] = [{'path': 'Other'}]
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
