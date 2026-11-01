#!/usr/bin/env python3
"""Verify exact permitted loss and unexpected PDF write finding with independent readers."""
import argparse
import copy
import hashlib
import json
from pathlib import Path
import re

from jsonschema import Draft202012Validator
import rfc8785

NAMES = ('theme', 'theme-second', 'missing-link', 'plain')
PDF_NAMES = ('theme', 'theme-second', 'plain')


def sha(data):
    return hashlib.sha256(data).hexdigest()


def collect(fixtures, observed, independent, assembly, schema_log, schema_root):
    return {'manifest': json.loads((fixtures / 'manifest.json').read_text()),
            'inputs': {path.name: sha(path.read_bytes()) for path in fixtures.glob('*.docx')},
            'report': json.loads((observed / 'report.json').read_text()), 'assembly': sha(assembly.read_bytes()),
            'plans': {name: (observed / (name + '-plan.json')).read_bytes() for name in NAMES},
            'outputs': {path.stem: sha(path.read_bytes()) for path in observed.glob('*.pdf')},
            'schemaLog': schema_log.read_text(),
            'planSchema': json.loads((schema_root / 'template-compatibility-plan.schema.json').read_text()),
            'policySchema': json.loads((schema_root / 'template-compatibility-policy.schema.json').read_text()),
            'pdfText': {name: (independent / (name + '.txt')).read_text().split() for name in PDF_NAMES},
            'pdfInfo': {name: (independent / (name + '-info.txt')).read_text() for name in PDF_NAMES},
            'pdfFonts': {name: (independent / (name + '-fonts.txt')).read_text() for name in PDF_NAMES}}


def check(data):
    expected = {'theme': 'Alpha123', 'theme-second': 'Bravo456', 'missing-link': 'Linked123', 'plain': 'Plain123'}
    report = data['report']
    if data['manifest']['expectedText'] != expected or data['manifest']['documents'] != data['inputs']:
        raise ValueError('Independent fixture manifest changed')
    if report['coreSha256'] != data['assembly'] or set(report['cases']) != set(NAMES) or set(data['outputs']) != set(PDF_NAMES):
        raise ValueError('Loaded core, scenario or output set changed')
    for name, digest in data['inputs'].items():
        if name + ' ' + digest + ' errors=0' not in data['schemaLog']:
            raise ValueError('Independent input schema validation missing')
    options = None
    for name, text in expected.items():
        case, raw = report['cases'][name], data['plans'][name]
        plan, policy = json.loads(raw), json.loads(case['policy'])
        if list(Draft202012Validator(data['planSchema']).iter_errors(plan)) or list(Draft202012Validator(data['policySchema']).iter_errors(policy)):
            raise ValueError('Plan or policy violates its design schema')
        digest = sha(rfc8785.dumps(plan))
        if raw != rfc8785.dumps(plan) or case['canonical'].encode() != raw or case['digest'] != digest or policy['planDigest'] != digest:
            raise ValueError('Canonical plan or policy binding changed')
        if plan['identities']['input'] != data['inputs'][name + '.docx'] or case['inputSha256'] != plan['identities']['input']:
            raise ValueError('Source identity changed')
        if case['ExpectedInputSha256'] != case['ObservedInputSha256'] or case['ExpectedInputSha256'] != case['inputSha256']:
            raise ValueError('Execution source identity changed')
        if options is not None and options != case['OptionsSha256']:
            raise ValueError('Fixed BestEffort options changed')
        options = case['OptionsSha256']
        if options != plan['identities']['options'] or not re.fullmatch('[0-9a-f]{64}', options):
            raise ValueError('Output options identity changed')
        if plan['stages'] != {'parse': 'performed', 'destination': 'performed', 'layout': 'performed', 'write': 'not-performed', 'validation': 'not-performed'}:
            raise ValueError('Plan stage claims changed')
        semantics = ([{'code': 'content.links', 'assessment': 'unsupported'}] if name == 'missing-link' else [])
        semantics.append({'code': 'content.text', 'assessment': 'supported'})
        if plan['requirements'] != [] or plan['semantics'] != semantics:
            raise ValueError('Bounded plan semantics changed')
        if policy['requiredSemantics'] != ['content.text'] or case['IndependentValidation'] != 'not-performed' or case['prefix'] != 42 or not case['streamsOpen']:
            raise ValueError('Policy, independent-validation or stream contract changed')
        theme = name.startswith('theme')
        location = {'partId': 'part-0001', 'childIndexes': [1, 1, 1, 1]}
        loss = {'code': 'Docx.ThemeFont', 'location': location}
        if policy['allowedLosses'] != ([loss] if theme else []):
            raise ValueError('Exact loss permission changed')
        expected_plan = {'code': 'Docx.ThemeFont', 'phase': 'parse', 'evidence': 'observed', 'severity': 'warning', 'count': 1,
                         'locationsTruncated': False, 'locations': [location], 'remediation': 'Remediation.SetExplicitFont'}
        if plan['findings'] != ([expected_plan] if theme else []):
            raise ValueError('Planned findings changed')
        expected_finding = {'Code': 'Docx.ThemeFont' if theme else 'Pdf.OmittedDestination', 'severity': 'Warning', 'Count': 1,
                            'LocationsTruncated': False, 'phases': ['Parse' if theme else 'Write'], 'SourceLocationsAvailable': False}
        if name == 'missing-link':
            if (case['State'], case['Code'], case['outputLength'], case['OutputSha256'], case['actualOutputSha256'], case['findings']) != (
                    'unexpected-finding', 'Policy.UnexpectedFinding', 0, None, None, [expected_finding]):
                raise ValueError('Unexpected write finding was not observed and contained')
        else:
            status = ('allowed-loss', 'Policy.AllowedLoss') if theme else ('completed', 'Policy.Completed')
            if (case['State'], case['Code']) != status or case['OutputSha256'] != data['outputs'][name] or case['actualOutputSha256'] != data['outputs'][name]:
                raise ValueError('Admitted outcome or output identity changed')
            if case['findings'] != ([expected_finding] if theme else []):
                raise ValueError('Observed diagnostic summary changed')
            if data['pdfText'][name] != [text] or not re.search(r'^Pages:\s+1\s*$', data['pdfInfo'][name], re.MULTILINE):
                raise ValueError('Independent PDF text or page count changed')
            fonts = data['pdfFonts'][name].splitlines()
            if len(fonts) != 3 or fonts[2].split()[:6] != ['NotoSans-Regular', 'TrueType', 'WinAnsi', 'yes', 'no', 'yes']:
                raise ValueError('Independent PDF font observation changed')
        if text in case['canonical'] or 'word/document.xml' in case['canonical']:
            raise ValueError('Plan exposed document text or source part names')
    rejection = {'State': 'policy-rejected', 'Code': 'Policy.LossNotAllowed', 'inputPosition': 0, 'unchangedOutput': True}
    required_link = {'State': 'policy-rejected', 'Code': 'Policy.RequiredSemanticUnsupported', 'inputPosition': 0, 'unchangedOutput': True}
    unknown_link = {'State': 'policy-rejected', 'Code': 'Policy.RequiredSemanticUnknown', 'inputPosition': 0, 'unchangedOutput': True}
    if report['negatives'] != {'missingPermission': rejection, 'wrongLocation': rejection, 'strictError': 'Plan.SourceProfile', 'requiredLink': required_link, 'unknownLink': unknown_link}:
        raise ValueError('Missing/wrong permission or Strict unexpectedly admitted loss')
    return {'schemaValidInputs': 4, 'allowedLossCases': 2, 'cleanBestEffortCases': 1, 'actualUnexpectedWriteFindings': 1,
            'independentPdfTextAndPageCountMatch': True, 'canonicalPlanAndPolicySchemasMatch': True,
            'limitations': 'Only the exact local theme-font fallback and one missing internal link profile are checked; output is not independently validated during execution.'}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('fixtures', 'observed', 'independent', 'assembly', 'schema_log', 'schema_root'):
        parser.add_argument(name, type=Path)
    args = parser.parse_args()
    data = collect(args.fixtures, args.observed, args.independent, args.assembly, args.schema_log, args.schema_root)
    result = check(data)
    controls = {}
    mutations = {
        'fixture hash': lambda d: d['manifest']['documents'].update({'theme.docx': '0' * 64}),
        'core identity': lambda d: d.update(assembly='0' * 64),
        'schema evidence': lambda d: d.update(schemaLog=''),
        'plan bytes': lambda d: d['plans'].update(theme=b'{}'),
        'plan digest': lambda d: d['report']['cases']['theme'].update(digest='0' * 64),
        'source hash': lambda d: d['report']['cases']['theme'].update(inputSha256='0' * 64),
        'options hash': lambda d: d['report']['cases']['theme'].update(OptionsSha256='0' * 64),
        'output hash': lambda d: d['report']['cases']['theme'].update(OutputSha256='0' * 64),
        'missing pdf': lambda d: d['outputs'].pop('theme'),
        'output text': lambda d: d['pdfText'].update(theme=['changed']),
        'output page': lambda d: d['pdfInfo'].update(theme='Pages: 2'),
        'output font': lambda d: d['pdfFonts'].update(theme='missing'),
        'independent validation': lambda d: d['report']['cases']['theme'].update(IndependentValidation='passed'),
        'stream ownership': lambda d: d['report']['cases']['theme'].update(streamsOpen=False),
        'loss code': lambda d: d['report']['cases']['theme']['findings'][0].update(Code='Docx.Other'),
        'loss count': lambda d: d['report']['cases']['theme']['findings'][0].update(Count=2),
        'loss phase': lambda d: d['report']['cases']['theme']['findings'][0].update(phases=['Write']),
        'missing loss permission': lambda d: d['report']['negatives']['missingPermission'].update(State='allowed-loss'),
        'wrong loss location': lambda d: d['report']['negatives']['wrongLocation'].update(unchangedOutput=False),
        'strict downgrade': lambda d: d['report']['negatives'].update(strictError='accepted'),
        'required semantic accepted': lambda d: d['report']['negatives']['requiredLink'].update(State='completed'),
        'required semantic mutated output': lambda d: d['report']['negatives']['requiredLink'].update(unchangedOutput=False),
        'unassessed semantic accepted': lambda d: d['report']['negatives']['unknownLink'].update(State='completed'),
        'unassessed semantic mutated output': lambda d: d['report']['negatives']['unknownLink'].update(unchangedOutput=False),
        'write not unexpected': lambda d: d['report']['cases']['missing-link'].update(State='completed'),
        'write leaked output': lambda d: d['report']['cases']['missing-link'].update(outputLength=1),
        'write phase': lambda d: d['report']['cases']['missing-link']['findings'][0].update(phases=['Parse']),
        'write code': lambda d: d['report']['cases']['missing-link']['findings'][0].update(Code='Pdf.Other'),
    }
    for name, mutate in mutations.items():
        altered = copy.deepcopy(data); mutate(altered)
        try:
            check(altered)
        except (ValueError, KeyError, IndexError, TypeError) as error:
            controls[name] = str(error)
        else:
            raise ValueError('Negative control unexpectedly passed: ' + name)
    print(json.dumps({'result': result, 'negativeControls': controls,
                      'controlMethod': 'Independent observations were altered in memory.'}, indent=2))


if __name__ == '__main__':
    main()
