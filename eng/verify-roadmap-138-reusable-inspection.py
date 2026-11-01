#!/usr/bin/env python3
"""Independently check the fixed in-process inspection snapshot and identities."""
import argparse
import copy
import datetime
import hashlib
import json
from pathlib import Path
import struct


def digest(data):
    return hashlib.sha256(data).hexdigest()


class Binary:
    def __init__(self):
        self.data = bytearray()

    def string(self, value):
        data = value.encode('utf-8')
        length = len(data)
        while length >= 128:
            self.data.append((length & 127) | 128)
            length >>= 7
        self.data.append(length)
        self.data.extend(data)

    def int32(self, value):
        self.data.extend(struct.pack('<i', value))

    def int64(self, value):
        self.data.extend(struct.pack('<q', value))

    def boolean(self, value):
        self.data.append(int(value))

    def sha(self):
        return digest(self.data)


def options(minutes=0):
    output = Binary()
    output.string('wordnet-reusable-docx-inspection-options-v1')
    for value in (2, 16777216, 33554432, 67108864, 1024, 4194304, 64, 1000):
        output.int32(value)
    time = datetime.datetime(2026, 11, 1, 12, 0) + datetime.timedelta(minutes=minutes)
    delta = time - datetime.datetime(1, 1, 1)
    output.int64((delta.days * 86400 + delta.seconds) * 10000000)
    for value in (False, False, False, False):
        output.boolean(value)  # column balancing and three typography switches
    for value in (0, 32, 0, 1000000, 2, 2, 2, 100000):
        output.int32(value)
    output.boolean(False)
    for value in (50000, 4194304):
        output.int32(value)
    output.boolean(False)
    output.int32(1)
    output.boolean(True)
    output.int32(100000)
    output.int32(700)
    output.boolean(False)
    output.boolean(True)
    output.int32(0)
    output.boolean(False)  # no PDF language
    output.int32(0)
    output.int32(0)
    output.boolean(False)
    output.boolean(True)
    output.int32(0)
    output.boolean(False)  # no math-family override
    output.string('Noto Sans')
    return output.sha()


def collect(fixture, observation, assembly, schema_log, root):
    resources = []
    for folder in (root / 'src/wordnet/Resources/Fonts', root / 'src/wordnet.Fonts/Resources'):
        resources.append({path.name: digest(path.read_bytes()) for path in folder.glob('*.ttf')})
    return {'fixture': digest(fixture.read_bytes()), 'observation': json.loads(observation.read_text()),
            'assembly': digest(assembly.read_bytes()), 'schemaLog': schema_log.read_text(), 'sourceFonts': resources}


def check(data):
    report = data['observation']
    plan, environment = report['plan'], report['environment']
    if data['fixture'] != '59f3f4cd3d5ddb12e0b391183252ec9da23fe2e4884c59b6c4c58206d1a89c18':
        raise ValueError('Independent input identity changed')
    if 'simple.docx ' + data['fixture'] + ' errors=0' not in data['schemaLog']:
        raise ValueError('Independent source schema validation missing')
    if report['inputSha256'] != data['fixture'] or plan['InputSha256'] != data['fixture'] or plan['Inspection']['InputSha256'] != data['fixture']:
        raise ValueError('Input fingerprint changed')
    if report['coreSha256'] != data['assembly']:
        raise ValueError('Loaded core assembly changed')
    declaration = {'paths': [{'path': 'Customer', 'required': True, 'scope': None, 'type': 4},
                             {'path': 'Customer.Code', 'required': True, 'scope': None, 'type': 0},
                             {'path': 'Customer.Name', 'required': True, 'scope': None, 'type': 0}], 'schemaVersion': 1}
    schema = digest(json.dumps(declaration, separators=(',', ':')).encode())
    if plan['DataSchemaSha256'] != schema or plan['Inspection']['DataSchemaSha256'] != schema:
        raise ValueError('Declaration fingerprint changed')
    if plan['OptionsSha256'] != options() or report['shiftedOptionsSha256'] != options(1):
        raise ValueError('Fixed profile or clock fingerprint changed')
    provider = Binary()
    provider.string('wordnet-reusable-docx-inspection-no-optional-provider-v1')
    if plan['ProvidersSha256'] != provider.sha():
        raise ValueError('Provider profile fingerprint changed')
    engine = Binary()
    for value in ('wordnet-reusable-docx-inspection-engine-v1', environment['core'], environment['module'], environment['runtime'], environment['os']):
        engine.string(value)
    engine.int32(environment['pointerSize'])
    engine.boolean(environment['os64'])
    if plan['EngineSha256'] != engine.sha():
        raise ValueError('Module/runtime descriptor fingerprint changed')
    fonts = Binary()
    fonts.string('wordnet-reusable-docx-inspection-fonts-v1')
    assemblies = environment['resources']
    if len(assemblies) != 2 or [len(x['fonts']) for x in assemblies] != [4, 20]:
        raise ValueError('Bundled font inventory changed')
    if assemblies[0]['assembly'] != environment['core'] or assemblies[0]['module'] != environment['module']:
        raise ValueError('Core module identity differs from resource owner')
    if not assemblies[1]['assembly'].startswith('wordnet.Fonts, '):
        raise ValueError('Unexpected bundled-font assembly')
    for index, assembly in enumerate(assemblies):
        fonts.string(assembly['assembly'])
        fonts.string(assembly['module'])
        names = [font['name'] for font in assembly['fonts']]
        if names != sorted(set(names)):
            raise ValueError('Resource order or uniqueness changed')
        actual = {}
        for font in assembly['fonts']:
            name = font['name'].rsplit('.', 2)[-2] + '.ttf'
            actual[name] = font['sha256']
            fonts.string(font['name'])
            fonts.string(font['sha256'])
        if actual != data['sourceFonts'][index]:
            raise ValueError('Loaded font bytes differ from independently hashed repository resources')
    if plan['FontsSha256'] != fonts.sha():
        raise ValueError('Font identity changed')
    inspection = plan['Inspection']
    findings = {item['Code']: item for item in inspection['Findings']}
    if inspection['Destination'] != 2 or inspection['IsReusable'] is not False or any(
            not findings[code]['Unknown'] for code in ('Plan.UninspectedCoverage', 'Plan.ReuseUnavailable', 'Plan.OwnerCoverage')):
        raise ValueError('Inspection coverage or reusable-policy limitation changed')
    if len(inspection['Requirements']) != 2 or any(x['DeclaredType'] != 0 or x['Required'] is not True or x['ScopeUnknown'] is not False
                                                   for x in inspection['Requirements']):
        raise ValueError('Declared requirements changed')
    serialized = json.dumps(plan)
    if any(value in serialized for value in ('Customer', 'word/document.xml')):
        raise ValueError('Serialized snapshot leaked a sensitive alias')
    if (report['sameInspection'], report['staleInput'], report['staleSchema'], report['schemaPosition'],
        report['limit'], report['cancellation'], report['cancelPosition'], report['streamsRemainOpen']) != (
            True, 'Plan.StaleInput', 'Plan.StaleSchema', 0, 'Limit.TemplateInspection', True, 0, True):
        raise ValueError('Reuse, stale, bounded or cancellation behavior changed')
    return {'independentSchemaValidInputs': 1, 'fingerprintsIndependentlyRecomputed': 6,
            'repositoryFontResourcesMatched': 24, 'staleAndBoundedNegativesPassed': True,
            'limitations': 'In-process inspection snapshot only; no portable plan, conversion authorization, loss policy or binary attestation.'}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('fixture', 'observation', 'assembly', 'schema_log', 'root'):
        parser.add_argument(name, type=Path)
    args = parser.parse_args()
    data = collect(args.fixture, args.observation, args.assembly, args.schema_log, args.root)
    result = check(data)
    controls = {}
    mutations = {
        'fixture': lambda d: d.update(fixture='0' * 64),
        'schema validation': lambda d: d.update(schemaLog=''),
        'input fingerprint': lambda d: d['observation']['plan'].update(InputSha256='0' * 64),
        'core binary': lambda d: d.update(assembly='0' * 64),
        'declaration fingerprint': lambda d: d['observation']['plan'].update(DataSchemaSha256='0' * 64),
        'options fingerprint': lambda d: d['observation']['plan'].update(OptionsSha256='0' * 64),
        'clock': lambda d: d['observation'].update(shiftedOptionsSha256=d['observation']['plan']['OptionsSha256']),
        'provider fingerprint': lambda d: d['observation']['plan'].update(ProvidersSha256='0' * 64),
        'module fingerprint': lambda d: d['observation']['plan'].update(EngineSha256='0' * 64),
        'runtime descriptor': lambda d: d['observation']['environment'].update(runtime='altered'),
        'font fingerprint': lambda d: d['observation']['plan'].update(FontsSha256='0' * 64),
        'font resource bytes': lambda d: d['observation']['environment']['resources'][0]['fonts'][0].update(sha256='0' * 64),
        'font inventory': lambda d: d['observation']['environment']['resources'][1]['fonts'].pop(),
        'coverage': lambda d: d['observation']['plan']['Inspection']['Findings'][0].update(Unknown=False),
        'reusable policy': lambda d: d['observation']['plan']['Inspection'].update(IsReusable=True),
        'requirement': lambda d: d['observation']['plan']['Inspection']['Requirements'][0].update(Required=False),
        'privacy': lambda d: d['observation']['plan']['Inspection']['Requirements'][0].update(PathId='Customer.Name'),
        'input stale': lambda d: d['observation'].update(staleInput='none'),
        'schema stale': lambda d: d['observation'].update(staleSchema='none'),
        'schema consumed': lambda d: d['observation'].update(schemaPosition=1),
        'limit': lambda d: d['observation'].update(limit='none'),
        'cancellation': lambda d: d['observation'].update(cancellation=False),
        'stream ownership': lambda d: d['observation'].update(streamsRemainOpen=False),
        'snapshot': lambda d: d['observation'].update(sameInspection=False),
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
