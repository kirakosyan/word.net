"""Validate the proposed examples, not a production plan compiler or policy engine."""
import copy
import hashlib
import json
from pathlib import Path
import unittest

from jsonschema import Draft202012Validator
import rfc8785


ROOT = Path(__file__).resolve().parents[1] / 'docs/design'


def unique_object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError('duplicate JSON key')
        result[key] = value
    return result


def load(name):
    return json.loads((ROOT / name).read_text(), object_pairs_hook=unique_object)


PLAN_SCHEMA = load('template-compatibility-plan.schema.json')
POLICY_SCHEMA = load('template-compatibility-policy.schema.json')
PLAN = load('template-compatibility-plan.example.json')
POLICY = load('template-compatibility-policy.example.json')


def location_key(location):
    return location['partId'], tuple(location['childIndexes'])


def sorted_unique(values):
    return values == sorted(set(values))


def example_accepts(plan, policy, strict=False):
    """Small closed oracle for the two design examples, not an implementation API."""
    if list(Draft202012Validator(PLAN_SCHEMA).iter_errors(plan)) or list(Draft202012Validator(POLICY_SCHEMA).iter_errors(policy)):
        return False
    if hashlib.sha256(rfc8785.dumps(plan)).hexdigest() != policy['planDigest']:
        return False
    requirements = [item['pathId'] for item in plan['requirements']]
    semantic_codes = [item['code'] for item in plan['semantics']]
    findings = [(item['code'], item['phase'], item['evidence'], item['severity']) for item in plan['findings']]
    permissions = [(item['code'], location_key(item['location'])) for item in policy['allowedLosses']]
    if not all(sorted_unique(values) for values in (requirements, semantic_codes, findings, permissions, policy['requiredSemantics'])):
        return False
    if any(item['required'] and (item['type'] == 'unknown' or item['owner'] == 'unknown' or item['support'] != 'supported') for item in plan['requirements']):
        return False
    # This example registry defines exactly one semantic and one admissible loss.
    semantics = {item['code']: item['assessment'] for item in plan['semantics']}
    if any(code != 'content.text' or semantics.get(code) != 'supported' for code in policy['requiredSemantics']):
        return False
    if plan['destinationFormat'] == 'docx' and plan['stages']['layout'] == 'performed':
        return False
    if any(code != 'Docx.ThemeFont' for code, _ in permissions):
        return False
    for item in plan['findings']:
        locations = [location_key(location) for location in item['locations']]
        if item['code'] != 'Docx.ThemeFont' or not sorted_unique(locations) or len(locations) > item['count'] or item['locationsTruncated'] or item['evidence'] == 'unknown':
            return False
        if item['phase'] == 'layout' and item['evidence'] == 'observed' and plan['stages']['layout'] != 'performed':
            return False
        if item['severity'] == 'warning':
            if strict or not locations or any((item['code'], location) not in permissions for location in locations):
                return False
    return True


def bind(plan, policy):
    policy['planDigest'] = hashlib.sha256(rfc8785.dumps(plan)).hexdigest()


class TemplatePlanDesignTests(unittest.TestCase):
    def setUp(self):
        self.plan = copy.deepcopy(PLAN)
        self.policy = copy.deepcopy(POLICY)

    def test_examples_and_metaschemas(self):
        Draft202012Validator.check_schema(PLAN_SCHEMA)
        Draft202012Validator.check_schema(POLICY_SCHEMA)
        self.assertTrue(example_accepts(self.plan, self.policy))
        self.assertFalse(example_accepts(self.plan, self.policy, strict=True))

    def test_duplicate_json_keys_are_invalid(self):
        with self.assertRaises(ValueError):
            json.loads('{"key":1,"key":2}', object_pairs_hook=unique_object)

    def test_shape_rejects_unintended_content_and_invalid_stages(self):
        for mutate in (
            lambda p: p.update(documentText='private'),
            lambda p: p['identities'].update(input='not-a-digest'),
            lambda p: p['stages'].update(validation='passed'),
            lambda p: p['requirements'][0].update(type='arbitrary-object'),
            lambda p: p['requirements'][0].update(pathId='customer-name'),
            lambda p: p['findings'][0]['locations'][0].update(partId='customer-file'),
        ):
            with self.subTest(mutate=mutate):
                plan = copy.deepcopy(self.plan); mutate(plan); bind(plan, self.policy)
                self.assertFalse(example_accepts(plan, self.policy))

    def test_stale_plan_and_location_mismatch_are_rejected(self):
        self.plan['identities']['input'] = 'a' * 64
        self.assertFalse(example_accepts(self.plan, self.policy))
        bind(self.plan, self.policy)
        self.policy['allowedLosses'][0]['location']['childIndexes'] = [9]
        self.assertFalse(example_accepts(self.plan, self.policy))

    def test_unknown_or_truncated_coverage_is_not_accepted(self):
        for mutate in (
            lambda p: p['requirements'][0].update(support='unknown'),
            lambda p: p['semantics'][0].update(assessment='unknown'),
            lambda p: p['findings'][0].update(evidence='unknown'),
            lambda p: p['findings'][0].update(locationsTruncated=True),
            lambda p: p['findings'][0].update(locations=[]),
            lambda p: p['findings'][0].update(phase='layout'),
        ):
            with self.subTest(mutate=mutate):
                plan = copy.deepcopy(self.plan); mutate(plan); bind(plan, self.policy)
                self.assertFalse(example_accepts(plan, self.policy))

    def test_duplicate_aliases_findings_and_permissions_are_rejected(self):
        for field in ('requirements', 'semantics', 'findings'):
            with self.subTest(field=field):
                plan = copy.deepcopy(self.plan); plan[field].append(copy.deepcopy(plan[field][0])); bind(plan, self.policy)
                self.assertFalse(example_accepts(plan, self.policy))
        self.policy['allowedLosses'].append(copy.deepcopy(self.policy['allowedLosses'][0]))
        self.assertFalse(example_accepts(self.plan, self.policy))

    def test_required_semantic_needs_a_known_assessment(self):
        self.policy['requiredSemantics'].append('review.history')
        self.assertFalse(example_accepts(self.plan, self.policy))
        self.policy['requiredSemantics'] = ['content.text']
        self.plan['semantics'] = []; bind(self.plan, self.policy)
        self.assertFalse(example_accepts(self.plan, self.policy))


if __name__ == '__main__':
    unittest.main()
