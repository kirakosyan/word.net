"""Protect evidence identity/completeness and deterministic independent reductions."""
import copy
import importlib.util
import io
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from zipfile import ZIP_DEFLATED, ZIP_STORED, ZipFile, ZipInfo

ROOT = Path(__file__).resolve().parents[1]


def module(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


baseline = module('baseline', ROOT / 'eng/refresh-fidelity-baseline.py')
comparison = module('comparison', ROOT / 'eng/run-comparison.py')


class FidelityBaselineTests(unittest.TestCase):
    def assert_package_contents_equal(self, expected, actual):
        # ZIP producer metadata and deflate streams vary by OS/zlib. Compare every
        # uncompressed byte, including XML whitespace and embedded image bytes.
        with ZipFile(io.BytesIO(expected)) as left, ZipFile(io.BytesIO(actual)) as right:
            self.assertEqual(left.namelist(), right.namelist())
            for name in left.namelist():
                self.assertEqual(left.read(name), right.read(name), 'Fixture part drift: ' + name)

    def setUp(self):
        self.binaries = {'wordnet.dll': 'a' * 64, 'wordnet.Fonts.dll': 'b' * 64}
        self.result = dict(documentId='example', engine='wordnet', status='measured', inputSha256='c' * 64,
            authoredChecks=dict(passed=True, pageCountMatches=True), knownLossChecks=dict(passed=True),
            editableContent=dict(passed=True), referenceChecks=[dict(status='compared',
                pageCountMatches=True, pageGeometryMatches=False, textAndOrderMatches=True,
                glyphPlacementPasses=True, textAndGeometryPassed=False, visualPassed=True)],
            measurement=dict(assemblies=[dict(name=n[:-4], sha256=h) for n, h in self.binaries.items()],
                samples=[dict(kind='cold-conversion', milliseconds=2.5), dict(kind='warm-conversion', milliseconds=1.0)],
                snapshot=dict(pages=[dict(width=100, height=200, text='Example', glyphs=[dict(text='E', x=1, y=2)])])))
        self.report = dict(sourceRevision='exact-revision', sourceDirty=False, manifestSha256='d' * 64,
                           tolerances=comparison.TOLERANCES, results=[self.result])
        self.manifest = dict(documents=[dict(id='example', path='example.docx')])

    def defects(self, report):
        return comparison.defect_report(report, self.manifest)

    def test_known_reference_failure_remains_failure(self):
        self.assertEqual(1, baseline.verify_measurements(self.report, self.defects(self.report), comparison, 1, self.binaries))
        self.assertEqual(['page-geometry'], self.defects(self.report)['findings'][0]['failedVerdicts'])

    def test_missing_duplicate_or_uncaptured_document_is_rejected(self):
        for change in ('missing', 'duplicate', 'reference'):
            report = copy.deepcopy(self.report)
            if change == 'missing': report['results'] = []
            elif change == 'duplicate': report['results'] *= 2
            else: report['results'][0]['referenceChecks'] = [dict(status='not-provided')]
            with self.subTest(change=change), self.assertRaises(ValueError):
                baseline.verify_measurements(report, self.defects(report), comparison, 1, self.binaries)

    def test_source_package_binary_substitution_is_rejected(self):
        for name in self.binaries:
            expected = dict(self.binaries, **{name: '0' * 64})
            with self.subTest(name=name), self.assertRaisesRegex(ValueError, 'unexpected binary'):
                baseline.verify_measurements(self.report, self.defects(self.report), comparison, 1, expected)

    def test_unused_embedded_font_assembly_is_not_claimed_as_executed(self):
        self.result['measurement']['assemblies'] = [a for a in self.result['measurement']['assemblies'] if a['name'] != 'wordnet.Fonts']
        self.assertEqual(1, baseline.verify_measurements(self.report, self.defects(self.report), comparison, 1, self.binaries))
        self.result['measurement']['assemblies'] = []
        with self.assertRaisesRegex(ValueError, 'unexpected binary'):
            baseline.verify_measurements(self.report, self.defects(self.report), comparison, 1, self.binaries)

    def test_compaction_retains_all_verdicts_and_raw_samples_without_mutation(self):
        compact = baseline.compact(self.report)
        self.assertIn('snapshot', self.result['measurement'])
        self.assertNotIn('snapshot', compact['results'][0]['measurement'])
        self.assertEqual(self.result['measurement']['samples'], compact['results'][0]['measurement']['samples'])
        self.assertEqual(self.result['referenceChecks'], compact['results'][0]['referenceChecks'])
        self.assertEqual(self.defects(self.report), self.defects(compact))

    @staticmethod
    def package(parts, compression=ZIP_STORED, system=0):
        stream = io.BytesIO()
        with ZipFile(stream, 'w') as archive:
            for name, content in parts.items():
                info = ZipInfo(name, (2000, 1, 1, 0, 0, 0))
                info.create_system = system
                archive.writestr(info, content, compress_type=compression)
        return stream.getvalue()

    def test_package_comparison_accepts_only_container_encoding_differences(self):
        parts = {'word/document.xml': b'<document>Example</document>', 'word/media/logo.png': b'\x89PNG\r\n\x1a\n'}
        original = self.package(parts)
        repackaged = self.package(parts, ZIP_DEFLATED, 3)
        self.assertNotEqual(original, repackaged)
        self.assert_package_contents_equal(original, repackaged)

    def test_package_comparison_rejects_changed_or_missing_parts(self):
        parts = {'word/document.xml': b'<document>Example</document>', 'word/media/logo.png': b'\x89PNG\r\n\x1a\n'}
        original = self.package(parts)
        for name in parts:
            for change in ('changed', 'missing'):
                changed = dict(parts)
                if change == 'changed': changed[name] += b'!'
                else: del changed[name]
                with self.subTest(part=name, change=change), self.assertRaises(AssertionError):
                    self.assert_package_contents_equal(original, self.package(changed, ZIP_DEFLATED, 3))

    def test_reductions_are_reproducible_and_do_not_modify_original_corpus(self):
        original = ROOT / 'tests/wordnet.Conversion.Tests/Fixtures/Comparison'
        before = {p.name: baseline.sha(p) for p in original.iterdir() if p.is_file()}
        with tempfile.TemporaryDirectory() as directory:
            dest = Path(directory)
            command = [sys.executable, str(ROOT / 'eng/generate-fidelity-reductions.py'), '--output', str(dest)]
            subprocess.run(command, check=True, capture_output=True)
            first = {p.name: p.read_bytes() for p in dest.iterdir()}
            subprocess.run(command, check=True, capture_output=True)
            self.assertEqual(first, {p.name: p.read_bytes() for p in dest.iterdir()})
            manifest = json.loads((dest / 'manifest.json').read_text())
            self.assertEqual(12, len(manifest['documents']))
            committed = ROOT / 'tests/wordnet.Conversion.Tests/Fixtures/Fidelity103'
            historical = json.loads((committed / 'manifest.json').read_text())
            receipt = json.loads((ROOT / 'docs/measurements/fidelity-103-baseline.json').read_text())
            self.assertEqual(historical, receipt['cohorts']['reductions']['manifest'])
            self.assertEqual(baseline.sha(committed / 'manifest.json'), receipt['cohorts']['reductions']['manifestSha256'])
            # Keep both archive identities verified; do not rewrite the measured
            # fixtures or historical evidence to match this host's ZIP encoding.
            for folder, value in ((dest, manifest), (committed, historical)):
                for document in value['documents']:
                    self.assertEqual(document['sha256'], baseline.sha(folder / document['path']))
            for document, measured in zip(manifest['documents'], historical['documents']):
                document['sha256'] = measured['sha256']
                self.assert_package_contents_equal(first[document['path']], (committed / measured['path']).read_bytes())
            self.assertEqual(historical, manifest)
            self.assertEqual(before, {p.name: baseline.sha(p) for p in original.iterdir() if p.is_file()})


if __name__ == '__main__':
    unittest.main()
