#!/usr/bin/env python3
"""Verify nested item scope, declarations, fallback and independent output."""
import argparse
import copy
import importlib.util
import json
from pathlib import Path

import fitz

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('previous', ROOT / 'eng/verify-roadmap-138-preflight-repeat-fields.py')
previous = importlib.util.module_from_spec(spec)
spec.loader.exec_module(previous)
CASES = ('required', 'fallback', 'missing-parent', 'optional-parent', 'wrong-parent', 'optional-leaf', 'null-parent', 'deep')
EXPECTED = {'required': ['root', 'child', 'other', 'end'], 'fallback': ['root', 'child', 'root', 'end'],
            'missing-parent': ['root', 'child', 'other', 'end'], 'optional-parent': ['root', 'end'],
            'wrong-parent': ['root', 'end'], 'optional-leaf': ['root', 'child', 'root', 'end'],
            'null-parent': ['root', 'root', 'end'], 'deep': ['deep', 'end']}
UNKNOWN = {'missing-parent', 'optional-parent', 'wrong-parent', 'optional-leaf'}
PARENT = {'missing-parent': 'Plan.RepeatFieldParentUnknown', 'optional-parent': 'Plan.RepeatFieldParentOptional',
          'wrong-parent': 'Plan.RepeatFieldParentTypeMismatch'}


def check(observations):
    for name in CASES:
        item = observations[name]; report = item['result']; requirements = report['requirements']; findings = report['findings']
        data = json.loads(item['fixture'])
        if name in ('fallback', 'optional-leaf') and ('Name' in data['Items'][1]['Customer'] or data['Customer']['Name'] != 'root'):
            raise ValueError('Fallback fixture changed: ' + name)
        if name in ('optional-parent', 'wrong-parent') and data['Items']:
            raise ValueError('Parent uncertainty no longer uses an empty collection: ' + name)
        if name == 'null-parent' and data['Items'][0]['Customer'] is not None:
            raise ValueError('Null parent fixture changed')
        if report['IsReusable'] or not any(f['Code'] == 'Plan.RepeatScope' and f['Unknown'] for f in findings):
            raise ValueError('Reuse or broader repeat coverage changed: ' + name)
        private = {'Customer', 'Customer.Name', 'Account', 'Account.Contact', 'Account.Contact.Name', 'Items',
                   'Name', 'root', 'child', 'other', 'deep', 'word/document.xml'}
        if any(value in private for value in previous.values(item['default']) if isinstance(value, str)):
            raise ValueError('Default report disclosed a name or value: ' + name)
        if report['after'] != EXPECTED[name] or item['mergedText'] != EXPECTED[name] or item['officeText'] != EXPECTED[name]:
            raise ValueError('Merge or independent office output changed: ' + name)
        path = 'Account.Contact.Name' if name == 'deep' else 'Customer.Name'
        repeated = previous.only(requirements, lambda r: r['path'] == path and r['scope'] == 'Items', 'Item requirement missing: ' + name)
        unknown = name in UNKNOWN
        if repeated['ScopeUnknown'] != unknown or repeated['Count'] != 1 or repeated['DeclaredType'] != (None if unknown else 0) or repeated['Required'] != (None if unknown else True):
            raise ValueError('Item scope, type, presence or count changed: ' + name)
        if name != 'deep':
            root = previous.only(requirements, lambda r: r['path'] == path and r['scope'] is None, 'Root requirement missing: ' + name)
            if root['ScopeUnknown'] or root['DeclaredType'] != 0 or root['Required'] is not True or root['Count'] != 1 or root['PathId'] != repeated['PathId']:
                raise ValueError('Root and item scopes were conflated: ' + name)
        scoped = [f for f in findings if f['Code'] == 'Plan.RepeatFieldScopeUnknown']
        if len(scoped) != int(unknown) or any(not f['Unknown'] or f['path'] != path or f['scope'] != 'Items' for f in scoped):
            raise ValueError('Unknown item scope finding changed: ' + name)
        parent = [f for f in findings if f['Code'].startswith('Plan.RepeatFieldParent')]
        if len(parent) != int(name in PARENT):
            raise ValueError('Parent uncertainty finding count changed: ' + name)
        if parent and (parent[0]['Code'] != PARENT[name] or not parent[0]['Unknown'] or parent[0]['path'] != 'Customer' or parent[0]['scope'] != 'Items'):
            raise ValueError('Parent uncertainty finding changed: ' + name)
        for finding in parent + scoped:
            target = previous.node_at(item['source'], finding['indices'])
            if finding['part'] != 'word/document.xml' or target.tag != previous.W + 'fldSimple' or target.get(previous.W + 'instr') != 'MERGEFIELD ' + path:
                raise ValueError('Nested field source position changed: ' + name)
        expected_data = []
        if name in ('fallback', 'null-parent'):
            expected_data = [{'Code': 'Data.MissingItemPath', 'path': 'Customer.Name', 'scope': 'Items',
                              'ItemIndex': 1 if name == 'fallback' else 0, 'Unknown': False}]
        if report['dataFindings'] != expected_data or report['HasErrors'] != bool(expected_data) or report['HasUnknowns']:
            raise ValueError('Data declaration finding changed: ' + name)
        default = item['default']['data']
        if default['HasErrors'] != report['HasErrors'] or default['HasUnknowns'] != report['HasUnknowns'] or len(default['Findings']) != len(expected_data):
            raise ValueError('Default data summary changed: ' + name)
    return {'scenarios': len(CASES), 'independentOfficeReads': len(CASES), 'dottedParentsAreExplicit': True,
            'rootFallbackAndDataViolationSeparated': True, 'emptyRepeatUncertaintyPreserved': True}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('fixtures', 'observations', 'office'):
        parser.add_argument(name, type=Path)
    args = parser.parse_args(); observations = {}
    for name in CASES:
        directory = args.observations / name
        item = {'source': previous.xml(args.fixtures / (name + '.docx')),
                'fixture': (args.fixtures / (name + '.json')).read_text(),
                'result': json.loads((directory / 'result.json').read_text()),
                'default': json.loads((directory / 'default.json').read_text()),
                'mergedText': [node.text or '' for node in previous.xml(directory / 'merged.docx').iter(previous.W + 't')]}
        with fitz.open(args.office / (name + '.pdf')) as document:
            item['officeText'] = ''.join(page.get_text() for page in document).split()
        observations[name] = item
    result = check(observations); controls = {}
    for name in ('known item marked unknown', 'known item type changed', 'known item presence changed', 'known count changed',
                 'item scope changed', 'root alias changed', 'root type changed', 'missing parent omitted',
                 'missing parent made definite', 'optional parent omitted', 'optional parent made definite',
                 'wrong parent omitted', 'wrong parent made definite', 'optional leaf marked known',
                 'unknown scope finding omitted', 'parent source part changed', 'parent source position changed',
                 'fallback error omitted', 'fallback item index changed', 'fallback item scope changed',
                 'fallback error summary changed', 'null parent accepted as leaf', 'default report leaks name',
                 'default data summary changed', 'saved document text changed', 'independent output changed',
                 'deep item marked unknown', 'reusable plan claimed'):
        changed = copy.deepcopy(observations)
        required = next(r for r in changed['required']['result']['requirements'] if r['path'] == 'Customer.Name' and r['scope'] == 'Items')
        root = next(r for r in changed['required']['result']['requirements'] if r['path'] == 'Customer.Name' and r['scope'] is None)
        missing = next(f for f in changed['missing-parent']['result']['findings'] if f['Code'] == PARENT['missing-parent'])
        optional = next(f for f in changed['optional-parent']['result']['findings'] if f['Code'] == PARENT['optional-parent'])
        wrong = next(f for f in changed['wrong-parent']['result']['findings'] if f['Code'] == PARENT['wrong-parent'])
        fallback = changed['fallback']['result']['dataFindings'][0]
        if name == 'known item marked unknown': required['ScopeUnknown'] = True
        if name == 'known item type changed': required['DeclaredType'] = 1
        if name == 'known item presence changed': required['Required'] = False
        if name == 'known count changed': required['Count'] = 2
        if name == 'item scope changed': required['scope'] = 'Orders'
        if name == 'root alias changed': root['PathId'] = 'path-other'
        if name == 'root type changed': root['DeclaredType'] = 1
        if name == 'missing parent omitted': changed['missing-parent']['result']['findings'].remove(missing)
        if name == 'missing parent made definite': missing['Unknown'] = False
        if name == 'optional parent omitted': changed['optional-parent']['result']['findings'].remove(optional)
        if name == 'optional parent made definite': optional['Unknown'] = False
        if name == 'wrong parent omitted': changed['wrong-parent']['result']['findings'].remove(wrong)
        if name == 'wrong parent made definite': wrong['Unknown'] = False
        if name == 'optional leaf marked known': next(r for r in changed['optional-leaf']['result']['requirements'] if r['scope'] == 'Items' and r['path'] == 'Customer.Name')['ScopeUnknown'] = False
        if name == 'unknown scope finding omitted': changed['missing-parent']['result']['findings'] = [f for f in changed['missing-parent']['result']['findings'] if f['Code'] != 'Plan.RepeatFieldScopeUnknown']
        if name == 'parent source part changed': missing['part'] = 'word/other.xml'
        if name == 'parent source position changed': missing['indices'][-1] += 1
        if name == 'fallback error omitted': changed['fallback']['result']['dataFindings'].clear()
        if name == 'fallback item index changed': fallback['ItemIndex'] = 0
        if name == 'fallback item scope changed': fallback['scope'] = 'Orders'
        if name == 'fallback error summary changed': changed['fallback']['result']['HasErrors'] = False
        if name == 'null parent accepted as leaf': changed['null-parent']['result']['dataFindings'].clear()
        if name == 'default report leaks name': changed['required']['default']['leak'] = 'Customer.Name'
        if name == 'default data summary changed': changed['fallback']['default']['data']['HasErrors'] = False
        if name == 'saved document text changed': changed['fallback']['mergedText'][2] = 'other'
        if name == 'independent output changed': changed['fallback']['officeText'][2] = 'other'
        if name == 'deep item marked unknown': next(r for r in changed['deep']['result']['requirements'] if r['scope'] == 'Items')['ScopeUnknown'] = True
        if name == 'reusable plan claimed': changed['required']['result']['IsReusable'] = True
        try:
            check(changed)
        except (ValueError, KeyError, TypeError, IndexError) as error:
            controls[name] = str(error)
        else:
            raise ValueError('Negative control unexpectedly passed: ' + name)
    print(json.dumps({'result': result, 'negativeControls': controls,
                      'controlMethod': 'Parsed requirements, source positions, data reports and output observations changed in memory.'}, indent=2))


if __name__ == '__main__':
    main()
