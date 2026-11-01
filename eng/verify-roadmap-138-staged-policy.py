#!/usr/bin/env python3
"""Check narrow staged policy execution with independent schemas, canonicalization and PDF readers."""
import argparse
import copy
import datetime
import hashlib
import json
from pathlib import Path
import re
import struct

from jsonschema import Draft202012Validator
import rfc8785

NAMES = ('short', 'maximum', 'single', 'lowercase', 'mixed')


def sha(data):
    return hashlib.sha256(data).hexdigest()


class Binary:
    def __init__(self):
        self.data = bytearray()

    def string(self, text):
        encoded = text.encode('utf-8')
        count = len(encoded)
        while count >= 128:
            self.data.append((count & 127) | 128)
            count >>= 7
        self.data.append(count)
        self.data.extend(encoded)

    def integer(self, value):
        self.data.extend(struct.pack('<i', value))

    def boolean(self, value):
        self.data.append(int(value))


def fixed_options():
    output = Binary()
    output.string('wordnet-reusable-docx-inspection-options-v2')
    for value in (3, 0, 16777216, 33554432, 67108864, 1024, 4194304, 64, 1000):
        output.integer(value)
    delta = datetime.datetime(2026, 11, 1, 12, 0) - datetime.datetime(1, 1, 1)
    output.data.extend(struct.pack('<q', (delta.days * 86400 + delta.seconds) * 10000000))
    for value in (False, False, False, False):
        output.boolean(value)
    for value in (0, 32, 0, 1000000, 2, 2, 2, 100000):
        output.integer(value)
    output.boolean(False)
    for value in (50000, 4194304):
        output.integer(value)
    output.boolean(False)
    output.integer(1)
    output.boolean(True)
    output.integer(100000)
    output.integer(700)
    output.boolean(False)
    output.boolean(True)
    output.integer(0)
    output.boolean(False)
    output.integer(0)
    output.integer(0)
    output.boolean(False)
    output.boolean(True)
    output.integer(0)
    output.boolean(False)
    output.string('Noto Sans')
    return sha(output.data)


def collect(fixtures, observed, independent, assembly, schema_log, schema_root):
    return {'manifest': json.loads((fixtures / 'manifest.json').read_text()),
            'inputs': {path.name: sha(path.read_bytes()) for path in fixtures.glob('*.docx')},
            'inputLengths': {path.stem: len(path.read_bytes()) for path in fixtures.glob('*.docx')},
            'report': json.loads((observed / 'report.json').read_text()), 'assembly': sha(assembly.read_bytes()),
            'plans': {name: (observed / (name + '-plan.json')).read_bytes() for name in NAMES},
            'outputs': {path.stem: sha(path.read_bytes()) for path in observed.glob('*.pdf')},
            'schemaLog': schema_log.read_text(),
            'planSchema': json.loads((schema_root / 'template-compatibility-plan.schema.json').read_text()),
            'policySchema': json.loads((schema_root / 'template-compatibility-policy.schema.json').read_text()),
            'pdfText': {name: (independent / (name + '.txt')).read_text().split() for name in NAMES},
            'pdfInfo': {name: (independent / (name + '-info.txt')).read_text() for name in NAMES},
            'pdfFonts': {name: (independent / (name + '-fonts.txt')).read_text() for name in NAMES}}


def check(data):
    expected = {'short': 'Alpha123', 'maximum': 'ABCDEFGHIJKLMNOPQRSTUVWXYZ12345678901234', 'single': 'A',
                'lowercase': 'abcdefghijklmnopqrstuvwxyz', 'mixed': 'A0b1C2d3E4f5G6h7I8j9K0l1M2n3O4p5Q6r7S8t9'}
    report = data['report']
    if data['manifest']['expectedAcceptedText'] != expected or len(data['inputs']) != 11 or data['manifest']['documents'] != data['inputs']:
        raise ValueError('Independent source manifest changed')
    if report['coreSha256'] != data['assembly'] or set(report['cases']) != set(expected) or set(data['outputs']) != set(expected):
        raise ValueError('Loaded assembly or output set changed')
    for name, digest in data['inputs'].items():
        if name + ' ' + digest + ' errors=0' not in data['schemaLog']:
            raise ValueError('Independent source schema validation missing')
    empty_schema_hash = sha(b'{"paths":[],"schemaVersion":1}')
    shared = None
    for name, text in expected.items():
        case = report['cases'][name]
        raw = data['plans'][name]
        plan, policy = json.loads(raw), json.loads(case['policy'])
        if list(Draft202012Validator(data['planSchema']).iter_errors(plan)) or list(Draft202012Validator(data['policySchema']).iter_errors(policy)):
            raise ValueError('Plan or policy violates its design schema')
        digest = sha(rfc8785.dumps(plan))
        if raw != rfc8785.dumps(plan) or case['canonical'].encode() != raw or case['digest'] != digest or policy['planDigest'] != digest:
            raise ValueError('Canonical plan or policy binding changed')
        identities = plan['identities']
        if identities['input'] != data['inputs'][name + '.docx'] or case['inputSha256'] != identities['input'] or plan['observedInputBytes'] != data['inputLengths'][name]:
            raise ValueError('Input fingerprint or length changed')
        if identities['dataSchema'] != empty_schema_hash or identities['options'] != fixed_options() or case['OptionsSha256'] != identities['options']:
            raise ValueError('Data declaration or fixed PDF options identity changed')
        for key in ('fonts', 'providers', 'engine'):
            if not re.fullmatch('[0-9a-f]{64}', identities[key]):
                raise ValueError('Environment identity is malformed')
        current = tuple(identities[key] for key in ('fonts', 'providers', 'engine', 'options', 'dataSchema'))
        if shared is not None and shared != current:
            raise ValueError('Fixed environment changed between cases')
        shared = current
        if plan['stages'] != {'parse': 'performed', 'destination': 'performed', 'layout': 'performed', 'write': 'not-performed', 'validation': 'not-performed'}:
            raise ValueError('Plan stage claims changed')
        if plan['requirements'] != [] or plan['findings'] != [] or plan['semantics'] != [{'code': 'content.text', 'assessment': 'supported'}]:
            raise ValueError('Bounded plan semantics changed')
        if policy['requiredSemantics'] != ['content.text'] or policy['allowedLosses'] != []:
            raise ValueError('Policy semantics or explicit loss permissions changed')
        if (case['State'], case['Code'], case['ExpectedInputSha256'], case['ObservedInputSha256'], case['IndependentValidation'], case['prefix'], case['streamsOpen']) != (
                'completed', 'Policy.Completed', identities['input'], identities['input'], 'not-performed', 42, True):
            raise ValueError('Staged execution result or stream ownership changed')
        if case['OutputSha256'] != data['outputs'][name] or case['actualOutputSha256'] != data['outputs'][name]:
            raise ValueError('Output identity changed')
        if text in case['canonical'] or 'word/document.xml' in case['canonical']:
            raise ValueError('Default plan exposed source content or part names')
        if data['pdfText'][name] != [text] or not re.search(r'^Pages:\s+1\s*$', data['pdfInfo'][name], re.MULTILINE):
            raise ValueError('Independent PDF text or page count changed')
        fonts = data['pdfFonts'][name].splitlines()
        if len(fonts) != 3 or fonts[2].split()[:6] != ['NotoSans-Regular', 'TrueType', 'WinAnsi', 'yes', 'no', 'yes']:
            raise ValueError('Independent PDF font observation changed')
    negatives = report['negatives']
    if negatives['changed'] != {'State': 'source-changed', 'Code': 'Plan.StaleInput', 'unchangedOutput': True, 'streamOpen': True}:
        raise ValueError('Changed source did not fail before output')
    if negatives['stale'] != {'State': 'policy-rejected', 'Code': 'Policy.StalePlan', 'unchangedOutput': True, 'position': 0}:
        raise ValueError('Stale policy did not fail before input or output')
    if negatives['unmatched'] != {'State': 'policy-rejected', 'Code': 'Policy.UnmatchedPermission', 'unchangedOutput': True, 'position': 0}:
        raise ValueError('Unmatched loss did not fail before input or output')
    if negatives['unknown'] != {'code': 'Policy.UnknownSemantic', 'unchangedOutput': True, 'position': 0}:
        raise ValueError('Unknown semantic did not fail before input or output')
    return {'schemaValidInputs': 11, 'stagedPdfCases': 5, 'canonicalPlanAndPolicySchemasMatch': True,
            'independentPdfTextAndPageCountMatch': True, 'failClosedRuntimeNegatives': 4,
            'limitations': 'Prototype public wrapper covers only single-run alphanumeric DOCX to strict PDF without loss or template data; live independent validation is not performed.'}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('fixtures', 'observed', 'independent', 'assembly', 'schema_log', 'schema_root'):
        parser.add_argument(name, type=Path)
    args = parser.parse_args()
    data = collect(args.fixtures, args.observed, args.independent, args.assembly, args.schema_log, args.schema_root)
    result = check(data)
    controls = {}
    mutations = {
        'fixture manifest': lambda d: d['manifest']['documents'].update({'short.docx': '0' * 64}),
        'loaded core': lambda d: d.update(assembly='0' * 64),
        'schema evidence': lambda d: d.update(schemaLog=''),
        'plan bytes': lambda d: d['plans'].update(short=b'{}'),
        'plan digest': lambda d: d['report']['cases']['short'].update(digest='0' * 64),
        'source hash': lambda d: d['report']['cases']['short'].update(inputSha256='0' * 64),
        'source length': lambda d: d['inputLengths'].update(short=1),
        'options hash': lambda d: d['report']['cases']['short'].update(OptionsSha256='0' * 64),
        'output hash': lambda d: d['report']['cases']['short'].update(OutputSha256='0' * 64),
        'output missing': lambda d: d['outputs'].pop('short'),
        'execution state': lambda d: d['report']['cases']['short'].update(State='conversion-failed'),
        'independent validation overstated': lambda d: d['report']['cases']['short'].update(IndependentValidation='passed'),
        'destination changed early': lambda d: d['report']['cases']['short'].update(prefix=0),
        'stream closed': lambda d: d['report']['cases']['short'].update(streamsOpen=False),
        'source leaked': lambda d: d['report']['cases']['short'].update(canonical=d['report']['cases']['short']['canonical'] + 'Alpha123'),
        'pdf text': lambda d: d['pdfText'].update(short=['changed']),
        'pdf pages': lambda d: d['pdfInfo'].update(short='Pages: 2'),
        'pdf font': lambda d: d['pdfFonts'].update(short='missing'),
        'changed source accepted': lambda d: d['report']['negatives']['changed'].update(State='completed'),
        'stale policy consumed input': lambda d: d['report']['negatives']['stale'].update(position=1),
        'unmatched loss accepted': lambda d: d['report']['negatives']['unmatched'].update(Code='Policy.Completed'),
        'unknown semantic accepted': lambda d: d['report']['negatives']['unknown'].update(code='accepted'),
    }
    for name, mutate in mutations.items():
        altered = copy.deepcopy(data)
        mutate(altered)
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
