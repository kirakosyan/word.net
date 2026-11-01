#!/usr/bin/env python3
"""Independently validate a bounded policy prototype against the design schema and RFC 8785."""
import argparse
import copy
import hashlib
import json
from pathlib import Path

from jsonschema import Draft202012Validator
import rfc8785


def sha(data):
    return hashlib.sha256(data).hexdigest()


def collect(input_path, observation, assembly, schema_root):
    raw = input_path.read_bytes()
    plan_raw = (schema_root / 'template-compatibility-plan.example.json').read_bytes()
    return {'inputSha256': sha(raw), 'policy': json.loads(raw), 'observation': json.loads(observation.read_text()),
            'assemblySha256': sha(assembly.read_bytes()),
            'policySchema': json.loads((schema_root / 'template-compatibility-policy.schema.json').read_text()),
            'planSchema': json.loads((schema_root / 'template-compatibility-plan.schema.json').read_text()),
            'plan': json.loads(plan_raw), 'planInputSha256': sha(plan_raw)}


def check(data):
    report, policy = data['observation'], data['policy']
    if list(Draft202012Validator(data['policySchema']).iter_errors(policy)) or list(Draft202012Validator(data['planSchema']).iter_errors(data['plan'])):
        raise ValueError('Design schema or example changed')
    plan_hash = sha(rfc8785.dumps(data['plan']))
    if plan_hash != policy['planDigest'] or report['PlanDigest'] != plan_hash:
        raise ValueError('Policy no longer binds the independent example plan')
    if (report['planInputSha256'], report['planSha256'], report['planCanonical'].encode()) != (
            data['planInputSha256'], plan_hash, rfc8785.dumps(data['plan'])):
        raise ValueError('Canonical plan bytes or identity changed')
    if report['admissions'] != ['Policy.Admitted', 'Policy.StrictLoss', 'Policy.StalePlan',
                                'Policy.SemanticUnknown', 'Policy.LocationUnknown', 'Policy.LossNotAllowed']:
        raise ValueError('Fail-closed admission observations changed')
    if report['inputSha256'] != data['inputSha256'] or report['coreSha256'] != data['assemblySha256']:
        raise ValueError('Input or loaded assembly changed')
    canonical = rfc8785.dumps(policy)
    if report['canonical'].encode() != canonical or report['canonicalSha256'] != sha(canonical):
        raise ValueError('Canonical RFC 8785 policy bytes or hash changed')
    unsorted = json.loads(report['unsortedPolicy'])
    if list(Draft202012Validator(data['policySchema']).iter_errors(unsorted)):
        raise ValueError('Unsorted policy no longer meets the design schema')
    unsorted['requiredSemantics'].sort()
    unsorted['allowedLosses'].sort(key=lambda item: (item['code'], item['location']['partId'], item['location']['childIndexes']))
    sorted_bytes = rfc8785.dumps(unsorted)
    if report['sortedCanonical'].encode() != sorted_bytes or report['sortedSha256'] != sha(sorted_bytes):
        raise ValueError('Canonical policy ordering or numeric normalization changed')
    if report['required'] != ['content.text'] or report['losses'] != [
            {'Code': 'Docx.ThemeFont', 'PartId': 'part-0001', 'childIndexes': [1, 2, 1]}]:
        raise ValueError('Policy permissions changed')
    if report['checks'] != ['Policy.Invalid', 'Policy.UnknownLoss', 'Policy.UnknownSemantic', 'Policy.Invalid', 'Template.Json']:
        raise ValueError('Malformed or unregistered input was not rejected')
    return {'independentlyValidatedSchemas': 2, 'rfc8785CanonicalBytesMatch': True, 'locationOrderingChecked': True,
            'planDigestMatchesIndependentExample': True, 'policyRuntimeNegatives': 5, 'admissionRuntimeNegatives': 5,
            'limitations': 'Internal design-example admission only; no live source binding, conversion or loss enforcement is implemented.'}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('input', 'observation', 'assembly', 'schema_root'):
        parser.add_argument(name, type=Path)
    args = parser.parse_args()
    data = collect(args.input, args.observation, args.assembly, args.schema_root)
    result = check(data)
    controls = {}
    mutations = {
        'policy input': lambda d: d.update(inputSha256='0' * 64),
        'loaded core': lambda d: d.update(assemblySha256='0' * 64),
        'plan identity': lambda d: d['observation'].update(PlanDigest='0' * 64),
        'plan contents': lambda d: d['plan']['identities'].update(input='0' * 64),
        'plan source identity': lambda d: d.update(planInputSha256='0' * 64),
        'plan canonical bytes': lambda d: d['observation'].update(planCanonical='{}'),
        'plan canonical hash': lambda d: d['observation'].update(planSha256='0' * 64),
        'strict loss accepted': lambda d: d['observation']['admissions'].__setitem__(1, 'Policy.Admitted'),
        'stale plan accepted': lambda d: d['observation']['admissions'].__setitem__(2, 'Policy.Admitted'),
        'unknown semantic accepted': lambda d: d['observation']['admissions'].__setitem__(3, 'Policy.Admitted'),
        'truncated location accepted': lambda d: d['observation']['admissions'].__setitem__(4, 'Policy.Admitted'),
        'mismatched loss accepted': lambda d: d['observation']['admissions'].__setitem__(5, 'Policy.Admitted'),
        'schema validation': lambda d: d['policy'].update(extra=True),
        'canonical bytes': lambda d: d['observation'].update(canonical='{}'),
        'canonical hash': lambda d: d['observation'].update(canonicalSha256='0' * 64),
        'sorted policy': lambda d: d['observation'].update(sortedCanonical='{}'),
        'sorted hash': lambda d: d['observation'].update(sortedSha256='0' * 64),
        'unsorted input': lambda d: d['observation'].update(unsortedPolicy='{}'),
        'required semantic': lambda d: d['observation'].update(required=[]),
        'allowed code': lambda d: d['observation']['losses'][0].update(Code='Docx.Other'),
        'part identity': lambda d: d['observation']['losses'][0].update(PartId='part-0002'),
        'child position': lambda d: d['observation']['losses'][0]['childIndexes'].append(7),
        'unknown loss accepted': lambda d: d['observation']['checks'].__setitem__(1, 'accepted'),
        'duplicate key accepted': lambda d: d['observation']['checks'].__setitem__(4, 'accepted'),
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
