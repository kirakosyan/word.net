#!/usr/bin/env python3
"""Compare field-refresh observations with independently authored XML locations."""
import argparse
import copy
import hashlib
import json
from pathlib import Path
import xml.etree.ElementTree as ET
from zipfile import ZipFile

W = '{http://schemas.openxmlformats.org/wordprocessingml/2006/main}'
CASES = ('marked', 'clear', 'absent', 'invalid', 'nonmerge', 'locked-repeat', 'complex', 'mixed')
CODES = {'Plan.FieldMarkedDirty', 'Plan.FieldDirtyUnknown'}
PRIVATE = {'Name', 'Items', 'word/document.xml', 'cached-private-value', 'private-invalid-value', 'MERGEFIELD Name'}


def values(value):
    if isinstance(value, dict):
        return [item for child in value.values() for item in values(child)]
    if isinstance(value, list):
        return [item for child in value for item in values(child)]
    return [value]


def expected_fields(path):
    with ZipFile(path) as package:
        root = ET.fromstring(package.read('word/document.xml'))
    result = []

    # The original complex contrast contains exactly one direct begin/separate/end
    # sequence in a single body paragraph. Check that assumption independently before
    # treating its begin marker as part of the newly admitted inspection subset.
    if path.stem == 'complex':
        body = root.find(W + 'body')
        paragraphs = [] if body is None else body.findall(W + 'p')
        markers = [] if len(paragraphs) != 1 else [child for run in paragraphs[0].findall(W + 'r')
                                                   for child in run.findall(W + 'fldChar')]
        if [item.get(W + 'fldCharType') for item in markers] != ['begin', 'separate', 'end']:
            raise ValueError('Complex contrast no longer has its expected direct field boundaries')

    def walk(element, indices, repeat=False):
        current_repeat = repeat or element.tag == W + 'sdt'
        if element.tag == W + 'fldSimple':
            # These authored fixtures contain only direct body/cell fields or a
            # one-paragraph root repeat. A second field attribute deliberately
            # places that repeat outside the admitted shape.
            if not (current_repeat and element.get(W + 'fldLock') is not None):
                dirty = element.get(W + 'dirty')
                code = ('Plan.FieldMarkedDirty' if dirty in ('true', '1', 'on') else
                        'Plan.FieldDirtyUnknown' if dirty not in (None, 'false', '0', 'off') else None)
                if code:
                    remediation = 'Remediation.RefreshField' if code == 'Plan.FieldMarkedDirty' else 'Remediation.ReviewFields'
                    result.append((code, remediation, True, 'word/document.xml', 'Items' if current_repeat else None, tuple(indices)))
        elif path.stem == 'complex' and element.tag == W + 'fldChar' and element.get(W + 'fldCharType') == 'begin':
            dirty = element.get(W + 'dirty')
            code = ('Plan.FieldMarkedDirty' if dirty in ('true', '1', 'on') else
                    'Plan.FieldDirtyUnknown' if dirty not in (None, 'false', '0', 'off') else None)
            if code:
                remediation = 'Remediation.RefreshField' if code == 'Plan.FieldMarkedDirty' else 'Remediation.ReviewFields'
                result.append((code, remediation, True, 'word/document.xml', None, tuple(indices)))
        for index, child in enumerate(element, 1):
            walk(child, indices + [index], current_repeat)

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
        if sorted(actual, key=repr) != expected_fields(fixtures / (name + '.docx')):
            raise ValueError('Flag, scope or XML location changed: ' + name)
        default_findings = [f for f in default['Findings'] if f['Code'] in CODES]
        actual_public = sorted([(f['Code'], f['Remediation'], f['Unknown'], tuple(f['indices'] or ()), f['scope'] is not None, f['part'] is not None) for f in relevant], key=repr)
        public = sorted([(f['Code'], f['Remediation'], f['Unknown'], tuple(f['SourceElementIndices'] or ()), f['ScopePathId'] is not None, f['PartId'] is not None) for f in default_findings], key=repr)
        if actual_public != public:
            raise ValueError('Default and resolved findings differ: ' + name)
        if name == 'nonmerge' and not any(f['Code'] == 'Plan.FieldCoverage' and f['Unknown'] for f in report['findings']):
            raise ValueError('Non-merge field coverage became certain')
        if name == 'locked-repeat' and not any(f['Code'] == 'Plan.RepeatScope' and f['Unknown'] for f in report['findings']):
            raise ValueError('Unsupported repeat coverage became certain')
    return {'scenarios': len(CASES), 'validFlagValues': 6, 'boundedComplexFlagAndLockedRepeatRemainUncertain': True,
            'positionsCheckedAgainstIndependentXml': True, 'defaultReportsRemainPrivate': True}


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
             'position changed', 'part changed', 'repeat scope changed', 'repeat scope removed',
             'reuse claimed', 'coverage omitted', 'invalid accepted', 'false marked dirty',
             'complex finding omitted', 'locked repeat claimed', 'default privacy leaked', 'default finding altered',
             'input identity changed', 'nonmerge coverage omitted')
    for name in names:
        changed = copy.deepcopy(observed)
        marked = [f for f in changed['marked']['result']['findings'] if f['Code'] == 'Plan.FieldMarkedDirty']
        root = next(f for f in marked if f['scope'] is None)
        repeated = next(f for f in marked if f['scope'] == 'Items')
        if name == 'finding omitted': changed['marked']['result']['findings'].remove(root)
        elif name == 'finding code changed': root['Code'] = 'Plan.FieldDirtyUnknown'
        elif name == 'remediation changed': root['Remediation'] = 'Remediation.ReviewFields'
        elif name == 'unknown made certain': root['Unknown'] = False
        elif name == 'position changed': root['indices'][-1] += 1
        elif name == 'part changed': root['part'] = 'other.xml'
        elif name == 'repeat scope changed': repeated['scope'] = 'Other'
        elif name == 'repeat scope removed': repeated['scope'] = None
        elif name == 'reuse claimed': changed['marked']['result']['IsReusable'] = True
        elif name == 'coverage omitted': changed['marked']['result']['findings'] = [f for f in changed['marked']['result']['findings'] if f['Code'] != 'Plan.UninspectedCoverage']
        elif name == 'invalid accepted': next(f for f in changed['invalid']['result']['findings'] if f['Code'] == 'Plan.FieldDirtyUnknown')['Code'] = 'Plan.FieldMarkedDirty'
        elif name == 'false marked dirty': changed['clear']['result']['findings'].append(copy.deepcopy(root))
        elif name == 'complex finding omitted':
            complex_finding = next(f for f in changed['complex']['result']['findings'] if f['Code'] == 'Plan.FieldMarkedDirty')
            changed['complex']['result']['findings'].remove(complex_finding)
        elif name == 'locked repeat claimed': changed['locked-repeat']['result']['findings'].append(copy.deepcopy(repeated))
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
