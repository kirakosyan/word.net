#!/usr/bin/env python3
"""Compare cell-control and invalid-path findings with independent XML and merges."""
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
    simple, merged, invalid = (values[key] for key in ('simple', 'merged', 'invalid'))
    if set(simple['positions']) != {'merge:Customer.Name', 'merge:Customer.Code'}:
        raise ValueError('Unexpected source controls')
    missing = [f for f in simple['result']['findings'] if f['Code'] == 'Plan.MissingPath']
    if len(missing) != 1 or missing[0]['path'] != 'Customer.Code' or missing[0]['indices'] != simple['positions']['merge:Customer.Code']:
        raise ValueError('Cell control path or source location differs from XML')
    if {r['path'] for r in simple['result']['requirements']} != {'Customer.Name', 'Customer.Code'}:
        raise ValueError('Cell control requirements differ')
    if simple['result']['error'] != 'Template.MissingValue' or simple['result']['before'] != simple['result']['afterFailure']:
        raise ValueError('Missing cell path does not match unchanged failed merge')
    if simple['result']['changes'] != 2 or simple['result']['afterMerge'] != ['Ada', 'Q42'] or output_text != ['Ada', 'Q42']:
        raise ValueError('Successful output differs from independent package read')
    if merged['result']['requirements'] or any(f['Code'] == 'Plan.MissingPath' for f in merged['result']['findings']):
        raise ValueError('Merged table was incorrectly treated as inspected')
    invalid_findings = [f for f in invalid['result']['findings'] if f['Code'] == 'Plan.InvalidControlPath']
    positions = sorted(invalid['positions'].values())
    if len(invalid_findings) != 2 or sorted(f['indices'] for f in invalid_findings) != positions or any(f['path'] is not None for f in invalid_findings):
        raise ValueError('Invalid control source locations differ from XML')
    for finding in missing + invalid_findings:
        if not re.fullmatch(r'part-[0-9]{4}', finding['PartId'] or ''):
            raise ValueError('Control part alias is absent or not opaque')
    if invalid['result']['error'] != 'Template.Path' or invalid['result']['before'] != invalid['result']['afterFailure']:
        raise ValueError('Invalid-path diagnostic does not match unchanged failed merge')
    for value in values.values():
        serialized = json.dumps(value['default'], sort_keys=True)
        if any(secret in serialized for secret in ('Customer', 'Hidden', 'Bad-Name', 'word/document.xml', 'sdtContent')):
            raise ValueError('Default report disclosed a source or path name')
        if value['result']['IsReusable'] or not any(f['Code'] == 'Plan.ControlCoverage' and f['Unknown'] for f in value['result']['findings']):
            raise ValueError('Control coverage or reuse boundary changed')
    if not any(f['Code'] == 'Plan.OwnerCoverage' and f['Unknown'] for f in merged['result']['findings']):
        raise ValueError('Merged owner coverage is not explicitly unknown')
    return {'cellControlLocated': True, 'failedMergesUnchanged': True, 'successfulControls': 2,
            'invalidControlsLocated': 2, 'mergedOwnerUnknown': True, 'defaultNamesDisclosed': False}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('sources', type=Path)
    parser.add_argument('outputs', type=Path)
    args = parser.parse_args()
    values = {name: read(name, args.sources, args.outputs) for name in ('simple', 'merged', 'invalid')}
    with ZipFile(args.outputs / 'simple/merged.docx') as package:
        root = ET.fromstring(package.read('word/document.xml'))
    output_text = [node.text or '' for node in root.iter(W + 't')]
    result = check(values, output_text)
    negatives = {}
    for name in ('wrong cell position', 'wrong path', 'changed failed merge', 'changed output', 'merged requirement',
                 'wrong invalid position', 'invalid path disclosure', 'leaked part', 'lost owner uncertainty', 'lost control uncertainty'):
        altered, text = copy.deepcopy(values), list(output_text)
        simple, merged, invalid = (altered[key]['result'] for key in ('simple', 'merged', 'invalid'))
        if name == 'wrong cell position': next(f for f in simple['findings'] if f['Code'] == 'Plan.MissingPath')['indices'][-1] += 1
        if name == 'wrong path': next(f for f in simple['findings'] if f['Code'] == 'Plan.MissingPath')['path'] = 'Other'
        if name == 'changed failed merge': simple['afterFailure'] = ['changed']
        if name == 'changed output': text[0] = 'changed'
        if name == 'merged requirement': merged['requirements'] = [{'path': 'Hidden'}]
        if name == 'wrong invalid position': next(f for f in invalid['findings'] if f['Code'] == 'Plan.InvalidControlPath')['indices'][-1] += 1
        if name == 'invalid path disclosure': altered['invalid']['default']['extra'] = 'Bad-Name'
        if name == 'leaked part': altered['simple']['default']['extra'] = 'word/document.xml'
        if name == 'lost owner uncertainty': merged['findings'] = [f for f in merged['findings'] if f['Code'] != 'Plan.OwnerCoverage']
        if name == 'lost control uncertainty': simple['findings'] = [f for f in simple['findings'] if f['Code'] != 'Plan.ControlCoverage']
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
