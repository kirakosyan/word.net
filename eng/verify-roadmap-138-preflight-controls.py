#!/usr/bin/env python3
"""Compare control preflight predictions with source XML and actual merge outcomes."""
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
    simple, image, formatted, invalid, repeat = (values[key] for key in ('simple', 'image', 'formatted', 'invalid', 'repeat'))
    if set(simple['positions']) != {'merge:Customer.Name', 'if:!Show'}:
        raise ValueError('Unexpected simple source controls')
    missing = [f for f in simple['result']['findings'] if f['Code'] == 'Plan.MissingPath']
    if len(missing) != 1 or missing[0]['path'] != 'Customer.Name' or missing[0]['indices'] != simple['positions']['merge:Customer.Name']:
        raise ValueError('Missing control path or source position differs from XML')
    if not re.fullmatch(r'part-[0-9]{4}', missing[0]['PartId'] or ''):
        raise ValueError('Control part alias is absent or not opaque')
    if {r['path'] for r in simple['result']['requirements']} != {'Customer.Name', 'Show'}:
        raise ValueError('Simple control requirements differ')
    if simple['result']['error'] != 'Template.MissingValue' or simple['result']['before'] != simple['result']['afterFailure']:
        raise ValueError('Missing-path prediction does not match the unchanged failed merge')
    if simple['result']['changes'] != 2 or simple['result']['afterMerge'] != ['Ada', 'cached'] or output_text != ['Ada', 'cached']:
        raise ValueError('Successful merged content differs from the independent package read')
    bad_type = [f for f in image['result']['findings'] if f['Code'] == 'Plan.PathType']
    if len(bad_type) != 1 or bad_type[0]['path'] != 'Logo' or bad_type[0]['indices'] != image['positions'].get('image:Logo'):
        raise ValueError('Image type finding differs from source')
    if image['result']['error'] != 'Template.Image' or image['result']['before'] != image['result']['afterFailure']:
        raise ValueError('Image type prediction does not match the unchanged failed merge')
    if formatted['result']['requirements'] or formatted['result']['changes'] != 1 or formatted['result']['afterFailure'] != ['Ada']:
        raise ValueError('Formatted control should stay uninspected although its merge executes')
    if not any(f['Code'] == 'Plan.InvalidControlPath' for f in invalid['result']['findings']) or invalid['result']['error'] != 'Template.Path':
        raise ValueError('Invalid control path differs from merge admission')
    if repeat['result']['requirements'] or not any(f['Code'] == 'Plan.RepeatScope' and f['Unknown'] for f in repeat['result']['findings']):
        raise ValueError('Repeated scope is not explicitly unknown')
    for value in values.values():
        serialized = json.dumps(value['default'], sort_keys=True)
        if any(secret in serialized for secret in ('Customer', 'Show', 'Logo', 'Hidden', 'Bad-Name', 'word/document.xml', 'sdtContent')):
            raise ValueError('Default report disclosed a source or path name')
        if value['result']['IsReusable'] or not any(f['Code'] == 'Plan.ControlCoverage' and f['Unknown'] for f in value['result']['findings']):
            raise ValueError('Control coverage or reuse boundary changed')
    return {'simpleControls': 2, 'imageTypeRejected': True, 'formattedControlUnknown': True,
            'invalidPathRejected': True, 'repeatScopeUnknown': True, 'defaultNamesDisclosed': False}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('sources', type=Path)
    parser.add_argument('outputs', type=Path)
    args = parser.parse_args()
    values = {name: read(name, args.sources, args.outputs) for name in ('simple', 'image', 'formatted', 'invalid', 'repeat')}
    with ZipFile(args.outputs / 'simple/merged.docx') as package:
        root = ET.fromstring(package.read('word/document.xml'))
    output_text = [node.text or '' for node in root.iter(W + 't')]
    result = check(values, output_text)
    negatives = {}
    for name in ('wrong source position', 'missing path', 'image type', 'changed failed merge', 'changed output',
                 'leaked path', 'leaked part', 'lost control coverage', 'invented repeat requirement', 'missing invalid diagnostic'):
        altered, text = copy.deepcopy(values), list(output_text)
        simple = altered['simple']['result']; image = altered['image']['result']
        if name == 'wrong source position': next(f for f in simple['findings'] if f['Code'] == 'Plan.MissingPath')['indices'][-1] += 1
        if name == 'missing path': next(f for f in simple['findings'] if f['Code'] == 'Plan.MissingPath')['path'] = 'Other'
        if name == 'image type': next(f for f in image['findings'] if f['Code'] == 'Plan.PathType')['Code'] = 'Other'
        if name == 'changed failed merge': simple['afterFailure'] = ['changed']
        if name == 'changed output': text[0] = 'changed'
        if name == 'leaked path': altered['simple']['default']['extra'] = 'Customer.Name'
        if name == 'leaked part': altered['simple']['default']['extra'] = 'word/document.xml'
        if name == 'lost control coverage': simple['findings'] = [f for f in simple['findings'] if f['Code'] != 'Plan.ControlCoverage']
        if name == 'invented repeat requirement': altered['repeat']['result']['requirements'] = [{'path': 'Hidden'}]
        if name == 'missing invalid diagnostic': altered['invalid']['result']['findings'] = [f for f in altered['invalid']['result']['findings'] if f['Code'] != 'Plan.InvalidControlPath']
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
