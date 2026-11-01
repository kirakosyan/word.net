#!/usr/bin/env python3
"""Independently compute canonical schema fingerprints and check report binding."""
import argparse
import copy
import hashlib
import json
from pathlib import Path

CASES = ('empty', 'base', 'reordered', 'type', 'required', 'scope', 'name', 'case', 'root-item', 'extra')


def declarations(name):
    if name == 'empty':
        return []
    root = [{'path': 'Items', 'required': True, 'scope': None, 'type': 5},
            {'path': 'customer' if name == 'case' else 'Customer', 'required': True, 'scope': None, 'type': 4},
            {'path': 'Customer', 'required': True, 'scope': 'Items', 'type': 4}]
    root.append({'path': 'Customer.Code' if name == 'name' else 'Customer.Name', 'required': name != 'required',
                 'scope': None if name == 'root-item' else 'Orders' if name == 'scope' else 'Items',
                 'type': 1 if name == 'type' else 0})
    if name == 'extra':
        root.append({'path': 'Amount', 'required': True, 'scope': None, 'type': 1})
    if name == 'reordered':
        root.reverse()
    return root


def fingerprint(paths):
    ordered = sorted(paths, key=lambda item: (item['scope'] is not None, item['scope'] or '', item['path']))
    canonical = json.dumps({'paths': ordered, 'schemaVersion': 1}, ensure_ascii=False, sort_keys=True, separators=(',', ':')).encode('utf-8')
    return hashlib.sha256(canonical).hexdigest()


def values(value):
    if isinstance(value, dict):
        return [item for child in value.values() for item in values(child)]
    if isinstance(value, list):
        return [item for child in value for item in values(child)]
    return [value]


def check(observations, source_hash):
    identities = {}
    for name in CASES:
        item = observations[name]; report = item['result']; default = item['default']
        expected = fingerprint(declarations(name))
        if report['identity'] != expected or report['templateSchemaSha256'] != expected or report['dataSchemaSha256'] != expected:
            raise ValueError('Schema identity or report binding changed: ' + name)
        if default['template']['DataSchemaSha256'] != expected or default['data']['DataSchemaSha256'] != expected:
            raise ValueError('Default report schema identity changed: ' + name)
        if default['template']['InputSha256'] != source_hash:
            raise ValueError('Input fingerprint changed: ' + name)
        if report['IsReusable'] or default['template']['IsReusable'] or not any(f['Code'] == 'Plan.ReuseUnavailable' and f['Unknown'] for f in default['template']['Findings']):
            raise ValueError('Schema identity incorrectly authorized reuse: ' + name)
        private = {'Customer', 'Customer.Name', 'Customer.Code', 'customer', 'Items', 'Orders', 'Amount', 'Name', 'root', 'child', 'word/document.xml'}
        if any(value in private for value in values(default) if isinstance(value, str)):
            raise ValueError('Default report disclosed a path, part or value: ' + name)
        identities[name] = report['identity']
    if identities['base'] != identities['reordered'] or len({identities[name] for name in CASES if name != 'reordered'}) != len(CASES) - 1:
        raise ValueError('Order invariance or declaration distinction changed')
    return {'schemas': len(CASES), 'orderInvariant': True, 'allSemanticChangesDistinct': True,
            'reportsBoundToSchema': True, 'reuseRemainsUnavailable': True}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source', type=Path)
    parser.add_argument('observations', type=Path)
    args = parser.parse_args(); observations = {}
    source_hash = hashlib.sha256(args.source.read_bytes()).hexdigest()
    for name in CASES:
        directory = args.observations / name
        observations[name] = {'result': json.loads((directory / 'result.json').read_text()),
                              'default': json.loads((directory / 'default.json').read_text())}
    result = check(observations, source_hash); controls = {}
    for name in ('empty schema changed', 'base digest changed', 'reordered digest changed', 'type change ignored',
                 'required change ignored', 'scope change ignored', 'path change ignored', 'case change ignored',
                 'root and item conflated', 'extra declaration ignored', 'template identity changed', 'data identity changed',
                 'default template identity changed', 'default data identity changed', 'input fingerprint changed',
                 'reuse claimed in result', 'reuse claimed in report', 'reuse warning omitted', 'schema name leaked',
                 'data value leaked', 'scope name leaked', 'source part leaked'):
        changed = copy.deepcopy(observations)
        if name == 'empty schema changed': changed['empty']['result']['identity'] = changed['base']['result']['identity']
        if name == 'base digest changed': changed['base']['result']['identity'] = '0' * 64
        if name == 'reordered digest changed': changed['reordered']['result']['identity'] = '0' * 64
        for case, control in (('type', 'type change ignored'), ('required', 'required change ignored'),
                              ('scope', 'scope change ignored'), ('name', 'path change ignored'),
                              ('case', 'case change ignored'), ('root-item', 'root and item conflated'), ('extra', 'extra declaration ignored')):
            if name == control: changed[case]['result']['identity'] = changed['base']['result']['identity']
        if name == 'template identity changed': changed['base']['result']['templateSchemaSha256'] = '0' * 64
        if name == 'data identity changed': changed['base']['result']['dataSchemaSha256'] = '0' * 64
        if name == 'default template identity changed': changed['base']['default']['template']['DataSchemaSha256'] = '0' * 64
        if name == 'default data identity changed': changed['base']['default']['data']['DataSchemaSha256'] = '0' * 64
        if name == 'input fingerprint changed': changed['base']['default']['template']['InputSha256'] = '0' * 64
        if name == 'reuse claimed in result': changed['base']['result']['IsReusable'] = True
        if name == 'reuse claimed in report': changed['base']['default']['template']['IsReusable'] = True
        if name == 'reuse warning omitted': changed['base']['default']['template']['Findings'] = [f for f in changed['base']['default']['template']['Findings'] if f['Code'] != 'Plan.ReuseUnavailable']
        if name == 'schema name leaked': changed['base']['default']['leak'] = 'Customer.Name'
        if name == 'data value leaked': changed['base']['default']['leak'] = 'child'
        if name == 'scope name leaked': changed['base']['default']['leak'] = 'Items'
        if name == 'source part leaked': changed['base']['default']['leak'] = 'word/document.xml'
        try:
            check(changed, source_hash)
        except (ValueError, KeyError, TypeError, IndexError) as error:
            controls[name] = str(error)
        else:
            raise ValueError('Negative control unexpectedly passed: ' + name)
    print(json.dumps({'result': result, 'negativeControls': controls,
                      'controlMethod': 'Independently canonicalized declaration sets and altered parsed reports.'}, indent=2))


if __name__ == '__main__':
    main()
