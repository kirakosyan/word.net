#!/usr/bin/env python3
"""Check bounded complex-field refresh flags against independently parsed OOXML."""
import argparse
import copy
import hashlib
import json
from pathlib import Path
import xml.etree.ElementTree as ET
from zipfile import ZipFile

W = '{http://schemas.openxmlformats.org/wordprocessingml/2006/main}'
CASES = ('marked', 'clear', 'absent', 'invalid', 'locked', 'cross', 'nested-simple', 'nested-complex', 'nested-simple-outer', 'nonmerge', 'repeated')
CODES = {'Plan.FieldMarkedDirty', 'Plan.FieldDirtyUnknown'}
PRIVATE = {'Name', 'Items', 'word/document.xml', 'cached-private-value', 'private-invalid-value', 'MERGEFIELD Name'}


def values(value):
    if isinstance(value, dict):
        return [item for child in value.values() for item in values(child)]
    if isinstance(value, list):
        return [item for child in value for item in values(child)]
    return [value]


def expected_fields(path, name):
    with ZipFile(path) as package:
        root = ET.fromstring(package.read('word/document.xml'))
    result = []

    def walk(element, indices):
        if name in ('marked', 'invalid', 'nonmerge') and element.tag == W + 'fldChar' and element.get(W + 'fldCharType') == 'begin':
            dirty = element.get(W + 'dirty')
            code = 'Plan.FieldMarkedDirty' if dirty in ('true', '1', 'on') else 'Plan.FieldDirtyUnknown'
            remediation = 'Remediation.RefreshField' if code == 'Plan.FieldMarkedDirty' else 'Remediation.ReviewFields'
            result.append((code, remediation, True, 'word/document.xml', None, tuple(indices)))
        for index, child in enumerate(element, 1):
            walk(child, indices + [index])

    walk(root, [1])
    return sorted(result, key=repr)


def check(fixtures, observations):
    for name in CASES:
        item = observations[name]
        report, default = item['result'], item['default']
        if report['IsReusable'] or default['IsReusable']:
            raise ValueError('Reuse incorrectly claimed: ' + name)
        if default['InputSha256'] != hashlib.sha256((fixtures / (name + '.docx')).read_bytes()).hexdigest():
            raise ValueError('Input fingerprint changed: ' + name)
        if any(value in PRIVATE for value in values(default) if isinstance(value, str)):
            raise ValueError('Default report exposed synthetic private data: ' + name)
        if not any(f['Code'] == 'Plan.UninspectedCoverage' and f['Unknown'] for f in report['findings']):
            raise ValueError('Uninspected coverage omitted: ' + name)
        relevant = [f for f in report['findings'] if f['Code'] in CODES]
        actual = [(f['Code'], f['Remediation'], f['Unknown'], f['part'], f['scope'], tuple(f['indices'] or ())) for f in relevant]
        if sorted(actual, key=repr) != expected_fields(fixtures / (name + '.docx'), name):
            raise ValueError('Flag or XML begin-marker location changed: ' + name)
        default_findings = [f for f in default['Findings'] if f['Code'] in CODES]
        actual_public = sorted([(f['Code'], f['Remediation'], f['Unknown'], tuple(f['indices'] or ()), f['scope'] is not None, f['part'] is not None) for f in relevant], key=repr)
        public = sorted([(f['Code'], f['Remediation'], f['Unknown'], tuple(f['SourceElementIndices'] or ()), f['ScopePathId'] is not None, f['PartId'] is not None) for f in default_findings], key=repr)
        if actual_public != public:
            raise ValueError('Default and resolved findings differ: ' + name)
        if name == 'nonmerge' and not any(f['Code'] == 'Plan.FieldCoverage' and f['Unknown'] for f in report['findings']):
            raise ValueError('Non-merge field coverage became certain')
        if name == 'repeated' and not any(f['Code'] == 'Plan.RepeatScope' and f['Unknown'] for f in report['findings']):
            raise ValueError('Unsupported repeat coverage became certain')
    return {'scenarios': len(CASES), 'positionsCheckedAgainstIndependentXml': True,
            'crossParagraphLockedNestedAndRepeatRemainUnknown': True, 'defaultReportsRemainPrivate': True}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('fixtures', type=Path)
    parser.add_argument('observations', type=Path)
    args = parser.parse_args()
    observed = {name: {kind: json.loads((args.observations / name / (kind + '.json')).read_text())
                       for kind in ('default', 'result')} for name in CASES}
    result = check(args.fixtures, observed)
    controls = {}
    names = ('finding omitted', 'finding code changed', 'remediation changed', 'unknown made certain',
             'body position changed', 'cell position changed', 'part changed', 'scope invented',
             'reuse claimed', 'coverage omitted', 'invalid accepted', 'false marked dirty', 'absent marked dirty',
             'locked claimed', 'cross claimed', 'nested simple claimed', 'nested complex claimed', 'nested outer simple claimed', 'repeat claimed',
             'default privacy leaked', 'default finding altered', 'input identity changed', 'nonmerge coverage omitted')
    for name in names:
        changed = copy.deepcopy(observed)
        marked = [f for f in changed['marked']['result']['findings'] if f['Code'] == 'Plan.FieldMarkedDirty']
        body, cell = marked
        if name == 'finding omitted': changed['marked']['result']['findings'].remove(body)
        elif name == 'finding code changed': body['Code'] = 'Plan.FieldDirtyUnknown'
        elif name == 'remediation changed': body['Remediation'] = 'Remediation.ReviewFields'
        elif name == 'unknown made certain': body['Unknown'] = False
        elif name == 'body position changed': body['indices'][-1] += 1
        elif name == 'cell position changed': cell['indices'][-1] += 1
        elif name == 'part changed': body['part'] = 'other.xml'
        elif name == 'scope invented': body['scope'] = 'Items'
        elif name == 'reuse claimed': changed['marked']['result']['IsReusable'] = True
        elif name == 'coverage omitted': changed['marked']['result']['findings'] = [f for f in changed['marked']['result']['findings'] if f['Code'] != 'Plan.UninspectedCoverage']
        elif name == 'invalid accepted': next(f for f in changed['invalid']['result']['findings'] if f['Code'] == 'Plan.FieldDirtyUnknown')['Code'] = 'Plan.FieldMarkedDirty'
        elif name in ('false marked dirty', 'absent marked dirty', 'locked claimed', 'cross claimed', 'nested simple claimed', 'nested complex claimed', 'nested outer simple claimed', 'repeat claimed'):
            target = {'false marked dirty': 'clear', 'absent marked dirty': 'absent', 'locked claimed': 'locked', 'cross claimed': 'cross',
                      'nested simple claimed': 'nested-simple', 'nested complex claimed': 'nested-complex', 'nested outer simple claimed': 'nested-simple-outer', 'repeat claimed': 'repeated'}[name]
            changed[target]['result']['findings'].append(copy.deepcopy(body))
        elif name == 'default privacy leaked': changed['marked']['default']['leak'] = 'Name'
        elif name == 'default finding altered': next(f for f in changed['marked']['default']['Findings'] if f['Code'] == 'Plan.FieldMarkedDirty')['Unknown'] = False
        elif name == 'input identity changed': changed['marked']['default']['InputSha256'] = '0' * 64
        elif name == 'nonmerge coverage omitted': changed['nonmerge']['result']['findings'] = [f for f in changed['nonmerge']['result']['findings'] if f['Code'] != 'Plan.FieldCoverage']
        try:
            check(args.fixtures, changed)
        except (ValueError, KeyError, TypeError, IndexError) as error:
            controls[name] = str(error)
        else:
            raise ValueError('Negative control unexpectedly passed: ' + name)
    print(json.dumps({'result': result, 'negativeControls': controls,
                      'controlMethod': 'Synthetic reports were altered in memory and compared with independently parsed OOXML.'}, indent=2))


if __name__ == '__main__':
    main()
