#!/usr/bin/env python3
"""Verify declared item scopes, fallback uncertainty and independent merges."""
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
CASES = ('required', 'optional', 'wrong-scope', 'two-scopes', 'nested', 'number', 'mismatch', 'mismatch-empty')
EXPECTED = {'required': ['root', 'child', 'other', 'end'], 'optional': ['root', 'child', 'root', 'end'],
            'wrong-scope': ['root', 'child', 'root', 'end'], 'two-scopes': ['first', 'second', 'root', 'end'],
            'nested': ['deep', 'end'], 'number': ['12.5', 'end'], 'mismatch': ['cached', 'end'], 'mismatch-empty': ['end']}


def check(observations):
    for name in CASES:
        item = observations[name]; report = item['result']; requirements = report['requirements']; findings = report['findings']
        if report['IsReusable'] or not any(f['Code'] == 'Plan.RepeatScope' and f['Unknown'] for f in findings):
            raise ValueError('Reuse or repeat coverage changed: ' + name)
        private = {'Name', 'Items', 'Orders', 'Other', 'Customer', 'Customer.Name', 'Amount', 'root', 'child', 'other', 'first', 'second', 'deep', 'word/document.xml'}
        if any(value in private for value in previous.values(item['default']) if isinstance(value, str)):
            raise ValueError('Default report disclosed private names or values: ' + name)
        if report['after'] != EXPECTED[name] or item['mergedText'] != EXPECTED[name] or item['officeText'] != EXPECTED[name]:
            raise ValueError('Merged text or independent office reading changed: ' + name)
        if name == 'mismatch':
            if report['error'] != 'Template.Scalar' or report['before'] != report['after']:
                raise ValueError('Failed merge or atomicity changed: ' + name)
        elif report['error'] is not None:
            raise ValueError('Unexpected merge failure: ' + name)
        path = 'Customer.Name' if name == 'nested' else 'Amount' if name in ('number', 'mismatch', 'mismatch-empty') else 'Name'
        scopes = ('Items', 'Orders') if name == 'two-scopes' else ('Items',)
        entries = [previous.only(requirements, lambda r: r['path'] == path and r['scope'] == scope, 'Scoped requirement missing: ' + name) for scope in scopes]
        for scope,entry in zip(scopes,entries):
            unknown = name in ('optional', 'wrong-scope', 'nested') or name == 'two-scopes' and scope == 'Orders'
            expected_type = 1 if name == 'number' else 4 if name in ('mismatch', 'mismatch-empty') else 0
            if entry['ScopeUnknown'] != unknown or entry['Count'] != 1 or entry['DeclaredType'] != (None if unknown else expected_type) or entry['Required'] != (None if unknown else True):
                raise ValueError('Scope certainty, type or count changed: ' + name + '/' + scope)
            matching = [f for f in findings if f['Code'] == 'Plan.RepeatFieldScopeUnknown' and f['path'] == path and f['scope'] == scope]
            if bool(matching) != unknown or any(not f['Unknown'] for f in matching):
                raise ValueError('Unknown-scope finding changed: ' + name + '/' + scope)
            for finding in matching:
                target = previous.node_at(item['source'], finding['indices'])
                if finding['part'] != 'word/document.xml' or target.tag != previous.W + 'fldSimple' or target.get(previous.W + 'instr') != 'MERGEFIELD ' + path:
                    raise ValueError('Unknown field source position changed: ' + name)
        if name in ('required','optional','wrong-scope'):
            root = previous.only(requirements, lambda r: r['path'] == 'Name' and r['scope'] is None, 'Root field missing: ' + name)
            if root['ScopeUnknown'] or root['PathId'] != entries[0]['PathId'] or root['DeclaredType'] != 0 or root['Required'] is not True:
                raise ValueError('Root and item requirements were conflated: ' + name)
        if name == 'two-scopes' and entries[0]['PathId'] != entries[1]['PathId']:
            raise ValueError('Same path aliases changed across repeat scopes')
        mismatch = [f for f in findings if f['Code'] == 'Plan.RepeatFieldTypeMismatch']
        if name in ('mismatch','mismatch-empty'):
            if len(mismatch) != 1 or not mismatch[0]['Unknown'] or mismatch[0]['path'] != 'Amount' or mismatch[0]['scope'] != 'Items':
                raise ValueError('Conditional item type mismatch changed: ' + name)
            target = previous.node_at(item['source'], mismatch[0]['indices'])
            if mismatch[0]['part'] != 'word/document.xml' or target.tag != previous.W + 'fldSimple' or target.get(previous.W + 'instr') != 'MERGEFIELD Amount':
                raise ValueError('Mismatch source position changed: ' + name)
        elif mismatch:
            raise ValueError('Unexpected item type mismatch: ' + name)
    return {'scenarios': len(CASES), 'mergedOfficeReads': len(CASES), 'scopesSeparated': True,
            'optionalFallbackUnknown': True, 'emptyMismatchUnknown': True}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('sources','observations','office'):
        parser.add_argument(name, type=Path)
    args = parser.parse_args(); observations = {}
    for name in CASES:
        directory = args.observations/name
        item = {'source':previous.xml(args.sources/(name+'.docx')), 'result':json.loads((directory/'result.json').read_text()),
                'default':json.loads((directory/'default.json').read_text()),
                'mergedText':[node.text or '' for node in previous.xml(directory/'merged.docx').iter(previous.W+'t')]}
        with fitz.open(args.office/(name+'.pdf')) as document:
            item['officeText'] = ''.join(page.get_text() for page in document).split()
        observations[name] = item
    result = check(observations); controls = {}
    for name in ('required marked unknown','required type changed','required presence changed','required count changed','wrong scope',
                 'root alias changed','optional marked certain','optional type guessed','unknown finding missing','wrong source position',
                 'wrong source part','mismatch marked certain','mismatch finding missing','empty mismatch lost','merge changed',
                 'failed edit changed','private value leaked','reuse claimed','nested scope guessed','two scopes conflated'):
        changed = copy.deepcopy(observations)
        required = next(r for r in changed['required']['result']['requirements'] if r['path']=='Name' and r['scope']=='Items')
        optional = next(r for r in changed['optional']['result']['requirements'] if r['path']=='Name' and r['scope']=='Items')
        finding = next(f for f in changed['optional']['result']['findings'] if f['Code']=='Plan.RepeatFieldScopeUnknown')
        mismatch = next(f for f in changed['mismatch']['result']['findings'] if f['Code']=='Plan.RepeatFieldTypeMismatch')
        if name == 'required marked unknown': required['ScopeUnknown'] = True
        if name == 'required type changed': required['DeclaredType'] = 1
        if name == 'required presence changed': required['Required'] = False
        if name == 'required count changed': required['Count'] = 2
        if name == 'wrong scope': required['scope'] = 'Orders'
        if name == 'root alias changed': next(r for r in changed['required']['result']['requirements'] if r['path']=='Name' and r['scope'] is None)['PathId'] = 'path-other'
        if name == 'optional marked certain': optional['ScopeUnknown'] = False
        if name == 'optional type guessed': optional['DeclaredType'] = 0
        if name == 'unknown finding missing': changed['optional']['result']['findings'].remove(finding)
        if name == 'wrong source position': finding['indices'][-1] += 1
        if name == 'wrong source part': finding['part'] = 'word/other.xml'
        if name == 'mismatch marked certain': mismatch['Unknown'] = False
        if name == 'mismatch finding missing': changed['mismatch']['result']['findings'].remove(mismatch)
        if name == 'empty mismatch lost': changed['mismatch-empty']['result']['findings'] = [f for f in changed['mismatch-empty']['result']['findings'] if f['Code'] != 'Plan.RepeatFieldTypeMismatch']
        if name == 'merge changed': changed['required']['officeText'][1] = 'changed'
        if name == 'failed edit changed': changed['mismatch']['result']['before'] = ['changed']
        if name == 'private value leaked': changed['required']['default']['leak'] = 'Name'
        if name == 'reuse claimed': changed['required']['result']['IsReusable'] = True
        if name == 'nested scope guessed': next(r for r in changed['nested']['result']['requirements'] if r['path']=='Customer.Name')['ScopeUnknown'] = False
        if name == 'two scopes conflated': next(r for r in changed['two-scopes']['result']['requirements'] if r['scope']=='Orders' and r['path']=='Name')['scope'] = 'Items'
        try:
            check(changed)
        except (ValueError,KeyError,TypeError,IndexError) as error:
            controls[name] = str(error)
        else:
            raise ValueError('Negative control unexpectedly passed: ' + name)
    print(json.dumps({'result':result,'negativeControls':controls,
                      'controlMethod':'Parsed requirements, source positions and output observations changed in memory.'},indent=2))


if __name__ == '__main__':
    main()
