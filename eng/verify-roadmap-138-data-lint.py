#!/usr/bin/env python3
"""Run the local lint example against independently authored input contrasts."""
import argparse
import copy
import json
from pathlib import Path
import subprocess

CASES = ('template-only', 'valid', 'missing', 'wrong-type', 'undeclared', 'duplicate', 'bad-utf8', 'oversized', 'missing-file', 'bad-docx', 'bad-xml')
FAILURES = {'duplicate', 'bad-utf8', 'oversized', 'missing-file', 'bad-docx', 'bad-xml'}
EXPECTED_ERROR = 'Template lint could not inspect the supplied inputs.\n'


def values(value):
    if isinstance(value, dict):
        return [item for child in value.values() for item in values(child)]
    if isinstance(value, list):
        return [item for child in value for item in values(child)]
    return [value]


def check(observations):
    for name in CASES:
        item = observations[name]
        expected_exit = 1 if name in ('template-only', 'valid') else 2
        if item['exit'] != expected_exit:
            raise ValueError('Exit status changed: ' + name)
        if name in FAILURES:
            if item['stdout'] != '' or item['stderr'] != EXPECTED_ERROR or item['report'] is not None:
                raise ValueError('Failed input leaked data or returned a partial report: ' + name)
            continue
        if item['stderr'] != '' or item['report'] is None:
            raise ValueError('Unexpected lint error: ' + name)
        report = item['report']
        template = report if name == 'template-only' else report['template']
        if template['IsReusable'] or not any(f['Code'] == 'Plan.UninspectedCoverage' and f['Unknown'] for f in template['Findings']):
            raise ValueError('Unknown template coverage changed: ' + name)
        private = {'Customer', 'Customer.Name', 'Total', 'Hidden', 'lintprivate', 'not-number', 'word/document.xml'}
        if any(value in private for value in values(report) if isinstance(value, str)):
            raise ValueError('Default JSON exposed a path, part or value: ' + name)
        if name == 'template-only':
            if 'data' in report or 'template' in report:
                raise ValueError('Template-only output shape changed')
            continue
        findings = report['data']['Findings']
        expected = {'missing': ('Data.MissingPath', 'Remediation.ProvidePath'),
                    'wrong-type': ('Data.PathType', 'Remediation.FixDataType')}.get(name)
        if expected:
            if len(findings) != 1 or (findings[0]['Code'], findings[0]['Remediation']) != expected or findings[0]['Unknown'] or findings[0]['ItemIndex'] is not None:
                raise ValueError('Data finding or remediation changed: ' + name)
        elif findings:
            raise ValueError('Unexpected data finding: ' + name)
        if report['data']['HasErrors'] != bool(expected) or report['data']['HasUnknowns']:
            raise ValueError('Data summary changed: ' + name)
        missing_template = any(f['Code'] == 'Plan.MissingPath' and not f['Unknown'] for f in template['Findings'])
        if missing_template != (name == 'undeclared'):
            raise ValueError('Undeclared template path finding changed: ' + name)
    return {'scenarios': len(CASES), 'templateOnlyOutputPreserved': True,
            'malformedAndOversizedInputsPublishNoPartialReport': True, 'privacySafeDefaults': True}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('program', type=Path)
    parser.add_argument('fixtures', type=Path)
    parser.add_argument('--output', type=Path, help='New scratch directory for synthetic process observations.')
    args = parser.parse_args(); observations = {}
    for name in CASES:
        template = args.fixtures / ('undeclared.docx' if name == 'undeclared' else name + '.docx' if name in ('bad-docx', 'bad-xml') else 'template.docx')
        command = ['dotnet', str(args.program), str(template)]
        if name != 'template-only':
            data = 'valid' if name in ('undeclared', 'bad-docx', 'bad-xml') else 'missing-local-input' if name == 'missing-file' else name
            command.append(str(args.fixtures / (data + '.json')))
        completed = subprocess.run(command, capture_output=True, text=True, timeout=60, check=False)
        report = json.loads(completed.stdout) if completed.stdout else None
        observations[name] = {'exit': completed.returncode, 'stdout': completed.stdout, 'stderr': completed.stderr, 'report': report}
    result = check(observations); controls = {}
    for name in ('valid exit changed', 'template-only exit changed', 'template-only shape changed', 'unknown coverage removed',
                 'reuse claimed', 'missing data accepted', 'missing code changed', 'missing remediation changed',
                 'missing made unknown', 'wrong type accepted', 'wrong type remediation changed', 'wrong type item index changed',
                 'data error summary changed', 'data unknown summary changed', 'undeclared path accepted',
                 'private path leaked', 'private value leaked', 'duplicate emitted partial report', 'duplicate error leaked',
                 'invalid encoding accepted', 'oversized input accepted', 'missing file path leaked',
                 'malformed package accepted', 'malformed XML leaked'):
        changed = copy.deepcopy(observations)
        missing = changed['missing']['report']['data']['Findings'][0]
        wrong = changed['wrong-type']['report']['data']['Findings'][0]
        if name == 'valid exit changed': changed['valid']['exit'] = 0
        if name == 'template-only exit changed': changed['template-only']['exit'] = 0
        if name == 'template-only shape changed': changed['template-only']['report']['data'] = {}
        if name == 'unknown coverage removed': changed['valid']['report']['template']['Findings'] = [f for f in changed['valid']['report']['template']['Findings'] if f['Code'] != 'Plan.UninspectedCoverage']
        if name == 'reuse claimed': changed['valid']['report']['template']['IsReusable'] = True
        if name == 'missing data accepted': changed['missing']['exit'] = 1
        if name == 'missing code changed': missing['Code'] = 'Data.PathType'
        if name == 'missing remediation changed': missing['Remediation'] = 'Remediation.FixDataType'
        if name == 'missing made unknown': missing['Unknown'] = True
        if name == 'wrong type accepted': changed['wrong-type']['exit'] = 1
        if name == 'wrong type remediation changed': wrong['Remediation'] = 'Remediation.ProvidePath'
        if name == 'wrong type item index changed': wrong['ItemIndex'] = 0
        if name == 'data error summary changed': changed['missing']['report']['data']['HasErrors'] = False
        if name == 'data unknown summary changed': changed['valid']['report']['data']['HasUnknowns'] = True
        if name == 'undeclared path accepted': changed['undeclared']['report']['template']['Findings'] = [f for f in changed['undeclared']['report']['template']['Findings'] if f['Code'] != 'Plan.MissingPath']
        if name == 'private path leaked': changed['valid']['report']['leak'] = 'Customer.Name'
        if name == 'private value leaked': changed['valid']['report']['leak'] = 'lintprivate'
        if name == 'duplicate emitted partial report': changed['duplicate']['stdout'] = '{}'
        if name == 'duplicate error leaked': changed['duplicate']['stderr'] = 'lintprivate\n'
        if name == 'invalid encoding accepted': changed['bad-utf8']['exit'] = 1
        if name == 'oversized input accepted': changed['oversized']['exit'] = 1
        if name == 'missing file path leaked': changed['missing-file']['stderr'] = '/private/file.json\n'
        if name == 'malformed package accepted': changed['bad-docx']['exit'] = 1
        if name == 'malformed XML leaked': changed['bad-xml']['stderr'] = 'lintprivate\n'
        try:
            check(changed)
        except (ValueError, KeyError, TypeError, IndexError) as error:
            controls[name] = str(error)
        else:
            raise ValueError('Negative control unexpectedly passed: ' + name)
    if args.output is not None:
        args.output.mkdir(parents=True, exist_ok=False)
        for name, observation in observations.items():
            (args.output / (name + '.json')).write_text(json.dumps(observation, indent=2) + '\n')
    print(json.dumps({'result': result, 'negativeControls': controls,
                      'controlMethod': 'Observed process exit codes, default JSON and error output changed in memory.'}, indent=2))


if __name__ == '__main__':
    main()
