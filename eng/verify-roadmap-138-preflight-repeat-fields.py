#!/usr/bin/env python3
"""Verify repeat-field scope, numeric locations and independent merged output."""
import argparse
import copy
import json
from pathlib import Path
import xml.etree.ElementTree as ET
from zipfile import ZipFile

import fitz

W = '{http://schemas.openxmlformats.org/wordprocessingml/2006/main}'
CASES = ('both', 'item', 'empty-invalid', 'nonmerge', 'two-blocks', 'field-extra', 'wrong-type')
MERGED = {'both': ['root', 'child', 'root'], 'item': ['child', 'root'],
          'empty-invalid': ['end'], 'wrong-type': ['cached', 'end']}


def xml(path):
    with ZipFile(path) as archive:
        return ET.fromstring(archive.read('word/document.xml'))


def values(value):
    if isinstance(value, dict):
        return [item for child in value.values() for item in values(child)]
    if isinstance(value, list):
        return [item for child in value for item in values(child)]
    return [value]


def node_at(root, indices):
    if not indices or indices[0] != 1:
        raise ValueError('Invalid root position')
    for index in indices[1:]:
        children = list(root)
        if index < 1 or index > len(children):
            raise ValueError('Invalid child position')
        root = children[index - 1]
    return root


def only(items, predicate, message):
    matches = [item for item in items if predicate(item)]
    if len(matches) != 1:
        raise ValueError(message)
    return matches[0]


def check(observations):
    for name in CASES:
        item = observations[name]; report = item['result']; findings = report['findings']; requirements = report['requirements']
        if report['IsReusable'] or not any(f['Code'] == 'Plan.RepeatScope' and f['Unknown'] for f in findings):
            raise ValueError('Reuse or repeat coverage changed: ' + name)
        if not any(f['Code'] == 'Plan.OwnerCoverage' and f['Unknown'] for f in findings):
            raise ValueError('Owner coverage missing: ' + name)
        private = {'Name', 'Items', 'Code', 'Bad-Name', 'Secret', 'Other', 'word/document.xml', 'root', 'child'}
        if any(value in private for value in values(item['default']) if isinstance(value, str)):
            raise ValueError('Default report disclosed private names or values: ' + name)
        if name in ('both', 'item', 'wrong-type'):
            path = 'Code' if name == 'item' else 'Name'
            finding = only(findings, lambda f: f['Code'] == 'Plan.RepeatFieldScopeUnknown', 'Repeated field finding missing: ' + name)
            if not finding['Unknown'] or finding['path'] != path or finding['part'] != 'word/document.xml':
                raise ValueError('Repeated field certainty or alias changed: ' + name)
            target = node_at(item['source'], finding['indices'])
            if target.tag != W + 'fldSimple' or target.get(W + 'instr') != 'MERGEFIELD ' + path:
                raise ValueError('Repeated field source position changed: ' + name)
            repeated = only(requirements, lambda r: r['path'] == path and r['ScopeUnknown'], 'Unknown-scope requirement missing: ' + name)
            if repeated['DeclaredType'] is not None or repeated['Required'] is not None or repeated['Count'] != 1:
                raise ValueError('Repeated field guessed its type, presence or count: ' + name)
            if name == 'both':
                root = only(requirements, lambda r: r['path'] == 'Name' and not r['ScopeUnknown'], 'Root requirement missing')
                if root['PathId'] != repeated['PathId'] or root['DeclaredType'] != 0 or root['Required'] is not True or root['Count'] != 1:
                    raise ValueError('Root and repeated requirements were conflated')
        elif name in ('empty-invalid', 'nonmerge'):
            finding = only(findings, lambda f: f['Code'] == 'Plan.FieldCoverage', 'Uncertain field coverage missing: ' + name)
            if not finding['Unknown'] or node_at(item['source'], finding['indices']).tag != W + 'fldSimple':
                raise ValueError('Uncertain field coverage changed: ' + name)
            if any(f['Code'] == 'Plan.InvalidField' for f in findings) or any(r['ScopeUnknown'] for r in requirements):
                raise ValueError('Unexecuted field was declared invalid or resolved: ' + name)
        else:
            if any(f['Code'] == 'Plan.RepeatFieldScopeUnknown' for f in findings) or any(r['path'] in ('Secret', 'Other', 'Items') for r in requirements):
                raise ValueError('Unsupported repeat owner was inspected: ' + name)
        if name == 'wrong-type':
            finding = only(findings, lambda f: f['Code'] == 'Plan.PathType' and f['path'] == 'Items', 'Repeat type mismatch missing')
            if finding['Unknown'] or report['error'] != 'Template.Collection' or report['before'] != report['after']:
                raise ValueError('Repeat rejection or atomicity changed')
        elif report['error'] is not None:
            raise ValueError('Unexpected merge failure: ' + name)
        if name in MERGED and (report['after'] != MERGED[name] or item['mergedText'] != MERGED[name] or item['officeText'] != MERGED[name]):
            raise ValueError('Merged text or independent office reading changed: ' + name)
    return {'scenarios': len(CASES), 'mergedOfficeReads': len(MERGED), 'scopeKnownAndUnknownSeparated': True,
            'unparseableOrUnsupportedContentRemainsUnknown': True}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('sources', 'observations', 'office'):
        parser.add_argument(name, type=Path)
    args = parser.parse_args()
    observations = {}
    for name in CASES:
        directory = args.observations / name
        item = {'source': xml(args.sources / (name + '.docx')),
                'result': json.loads((directory / 'result.json').read_text()),
                'default': json.loads((directory / 'default.json').read_text())}
        if name in MERGED:
            item['mergedText'] = [node.text or '' for node in xml(directory / 'merged.docx').iter(W + 't')]
            with fitz.open(args.office / (name + '.pdf')) as document:
                item['officeText'] = ''.join(page.get_text() for page in document).split()
        observations[name] = item
    result = check(observations)
    controls = {}
    for name in ('scope certainty', 'guessed type', 'guessed presence', 'wrong count', 'different path alias', 'missing finding',
                 'wrong source position', 'wrong source part', 'root type changed', 'root scope changed', 'merged output changed',
                 'failed edit changed', 'private value leaked', 'unsupported owner inspected', 'invalid field marked certain', 'reuse claimed'):
        changed = copy.deepcopy(observations); both = changed['both']['result']; repeat = next(r for r in both['requirements'] if r['path'] == 'Name' and r['ScopeUnknown'])
        root = next(r for r in both['requirements'] if r['path'] == 'Name' and not r['ScopeUnknown'])
        finding = next(f for f in both['findings'] if f['Code'] == 'Plan.RepeatFieldScopeUnknown')
        if name == 'scope certainty': repeat['ScopeUnknown'] = False
        if name == 'guessed type': repeat['DeclaredType'] = 0
        if name == 'guessed presence': repeat['Required'] = True
        if name == 'wrong count': repeat['Count'] = 2
        if name == 'different path alias': repeat['PathId'] = 'path-other'
        if name == 'missing finding': both['findings'].remove(finding)
        if name == 'wrong source position': finding['indices'][-1] += 1
        if name == 'wrong source part': finding['part'] = 'word/other.xml'
        if name == 'root type changed': root['DeclaredType'] = 1
        if name == 'root scope changed': root['ScopeUnknown'] = True
        if name == 'merged output changed': changed['both']['officeText'][1] = 'other'
        if name == 'failed edit changed': changed['wrong-type']['result']['after'] = ['changed']
        if name == 'private value leaked': changed['both']['default']['leak'] = 'Name'
        if name == 'unsupported owner inspected': changed['two-blocks']['result']['requirements'].append({'path': 'Secret'})
        if name == 'invalid field marked certain': changed['empty-invalid']['result']['findings'].append({'Code': 'Plan.InvalidField'})
        if name == 'reuse claimed': both['IsReusable'] = True
        try:
            check(changed)
        except (ValueError, KeyError, TypeError, IndexError) as error:
            controls[name] = str(error)
        else:
            raise ValueError('Negative control unexpectedly passed: ' + name)
    print(json.dumps({'result': result, 'negativeControls': controls,
                      'controlMethod': 'Parsed report, source positions and output observations changed in memory.'}, indent=2))


if __name__ == '__main__':
    main()
