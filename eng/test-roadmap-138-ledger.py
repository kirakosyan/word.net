"""Check the ledger against the retained release measurements and failure boundaries."""
import copy
import importlib.util
import json
from pathlib import Path
import unittest
from unittest.mock import patch


ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('ledger', ROOT / 'eng/generate-roadmap-138-ledger.py')
ledger = importlib.util.module_from_spec(spec)
spec.loader.exec_module(ledger)


class ReleaseLedgerTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.data = ledger.build()

    def test_all_recorded_verdicts_are_indexed_once_per_run(self):
        findings = self.data['findings']
        keys = [(item['cohort'], item['mode'], item['documentId'], item['verdict']) for item in findings]
        self.assertEqual(312, len(keys))
        self.assertEqual(len(keys), len(set(keys)))
        counts = {}
        for cohort, (defect_template, _, _) in ledger.COHORTS.items():
            for mode in ('source', 'package'):
                path = ROOT / defect_template.format(mode=mode)
                defects = json.loads(path.read_text())
                counts[(cohort, mode)] = sum(len(item['failedVerdicts']) for item in defects['findings'])
        for key, expected in counts.items():
            self.assertEqual(expected, sum((item['cohort'], item['mode']) == key for item in findings))

    def test_reference_rounding_is_recognized_only_for_the_measured_geometry(self):
        source = [{'width': 595.3, 'height': 841.9}]
        reference = [{'width': 595.303937007874, 'height': 841.889763779528}]
        self.assertTrue(ledger.a4_rounding(source, reference))
        self.assertFalse(ledger.a4_rounding(source, []))
        self.assertFalse(ledger.a4_rounding(source + source, reference))
        self.assertFalse(ledger.a4_rounding(source, [{'width': 595.3, 'height': 840.0}]))
        for item in self.data['findings']:
            if item['cohort'] == 'contrast-existing' and item['verdict'] == 'page-geometry':
                self.assertIsNone(item['referenceSnapshot'])
                self.assertEqual('unknown', item['classification'])

    def test_paired_controls_and_workflow_probes_keep_their_limits(self):
        contrasts = [item for item in self.data['findings'] if item['cohort'] == 'contrast-theme-table']
        self.assertEqual('documented-unsupported', next(item['classification'] for item in contrasts
            if item['documentId'] == 'theme-font' and item['verdict'] == 'glyph-placement'))
        self.assertEqual('documented-unsupported', next(item['classification'] for item in contrasts
            if item['documentId'] == 'table-style' and item['verdict'] == 'raster'))
        self.assertEqual(6, len(self.data['workflowProbes']))
        self.assertTrue(all(item['classification'] == 'documented-unsupported' for item in self.data['workflowProbes']))
        self.assertEqual({'source', 'package'}, {item['mode'] for item in self.data['workflowProbes']})

    def test_missing_or_changed_evidence_cannot_be_silently_accepted(self):
        original = ledger.load

        def omit_failure(path):
            data, digest = original(path)
            if path == ledger.COHORTS['initial-existing'][0].format(mode='source'):
                data = copy.deepcopy(data); data['findings'].pop()
            return data, digest

        with patch.object(ledger, 'load', side_effect=omit_failure):
            with self.assertRaises(ValueError):
                ledger.build()

        def alter_pair(path):
            data, digest = original(path)
            if path.endswith('/pair-contrasts.json'):
                data = copy.deepcopy(data); data['observations'][0]['differentPixels'] = 1
            return data, digest

        with patch.object(ledger, 'load', side_effect=alter_pair):
            with self.assertRaises(ValueError):
                ledger.build()

        def alter_package(path):
            data, digest = original(path)
            if path == ledger.COHORTS['contrast-theme-table'][1].format(mode='package'):
                data = copy.deepcopy(data); data['results'][0]['measurement']['outputSha256'] = '0' * 64
            return data, digest

        with patch.object(ledger, 'load', side_effect=alter_package):
            with self.assertRaises(ValueError):
                ledger.build()

    def test_serialization_is_deterministic(self):
        first = ledger.serialize(self.data)
        self.assertEqual(self.data, json.loads(first))
        self.assertEqual(first, ledger.serialize(ledger.build()))

    def test_checked_in_ledger_matches_the_retained_reports(self):
        path = ROOT / 'docs/measurements/roadmap-138-failure-ledger.json'
        self.assertEqual(ledger.serialize(self.data), path.read_text(encoding='utf-8'))


if __name__ == '__main__':
    unittest.main()
