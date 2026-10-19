"""Negative controls exercise actual independent PDFs, extraction and raster metrics."""
import importlib.util
import copy
import contextlib
import io
import json
import os
from pathlib import Path
import shutil
import tempfile
import unittest
from unittest.mock import patch
from zipfile import ZipFile
from xml.etree import ElementTree

spec = importlib.util.spec_from_file_location('comparison', Path(__file__).with_name('run-comparison.py'))
comparison = importlib.util.module_from_spec(spec)
spec.loader.exec_module(comparison)
capture_spec = importlib.util.spec_from_file_location('capture_office', Path(__file__).with_name('capture-office-comparison.py'))
capture = importlib.util.module_from_spec(capture_spec)
capture_spec.loader.exec_module(capture)


def pdf(path, x=50, text='Invoice total 1200 EUR', pages=1):
    # Independent minimal PDF 1.4 writer: ISO 32000 text operators and xref offsets.
    objects = [b'<< /Type /Catalog /Pages 2 0 R >>', b'', b'<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>']
    kids = []
    for _ in range(pages):
        number = len(objects) + 1
        kids.append(f'{number} 0 R')
        objects.append(f'<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 200] /Resources << /Font << /F1 3 0 R >> >> /Contents {number + 1} 0 R >>'.encode())
        stream = f'BT /F1 12 Tf {x} 150 Td ({text}) Tj ET'.encode()
        objects.append(f'<< /Length {len(stream)} >>\nstream\n'.encode() + stream + b'\nendstream')
    objects[1] = f'<< /Type /Pages /Count {pages} /Kids [{" ".join(kids)}] >>'.encode()
    data = bytearray(b'%PDF-1.4\n'); offsets = [0]
    for i, obj in enumerate(objects, 1):
        offsets.append(len(data)); data.extend(f'{i} 0 obj\n'.encode() + obj + b'\nendobj\n')
    xref = len(data)
    data.extend(f'xref\n0 {len(objects) + 1}\n0000000000 65535 f \n'.encode())
    for offset in offsets[1:]:
        data.extend(f'{offset:010} 00000 n \n'.encode())
    data.extend(f'trailer << /Size {len(objects) + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n'.encode())
    path.write_bytes(data)


class ComparisonTests(unittest.TestCase):
    @staticmethod
    def report(results):
        return dict(sourceRevision='test-revision', sourceDirty=False, manifestSha256='0' * 64,
                    tolerances=comparison.TOLERANCES, results=results)

    @staticmethod
    def measured(name, engine='wordnet'):
        return dict(documentId=name, engine=engine, inputSha256='0' * 64, status='measured',
                    authoredChecks=dict(passed=True, pageCountMatches=True), knownLossChecks=dict(passed=True),
                    editableContent=dict(passed=True), referenceChecks=[dict(kind='word', status='compared',
                        pageCountMatches=True, textAndOrderMatches=True, glyphPlacementPasses=True,
                        pageGeometryMatches=True, textAndGeometryPassed=True, visualPassed=True)])

    @staticmethod
    def documents(results):
        return dict(documents=[dict(id=name, path=name + '.docx') for name in sorted({r['documentId'] for r in results})])

    def test_ranked_findings_retain_warning_and_editable_losses_and_engine_identity(self):
        warning = self.measured('warning'); warning['knownLossChecks']['passed'] = False
        editable = self.measured('editable'); editable['editableContent']['passed'] = False
        text = self.measured('text'); text['authoredChecks']['passed'] = False
        glyph = self.measured('glyph'); glyph['referenceChecks'][0].update(glyphPlacementPasses=False, textAndGeometryPassed=False)
        pages = self.measured('pages'); pages['authoredChecks']['pageCountMatches'] = False
        failed = self.measured('failed'); failed['status'] = 'failed'
        external = self.measured('warning', 'aspose'); external['authoredChecks']['passed'] = False
        results = [warning, editable, text, glyph, pages, failed, external, self.measured('passing')]
        report = self.report(results)
        defects = comparison.defect_report(report, self.documents(results))
        comparison.validate_defect_report(report, defects)
        self.assertEqual(7, len(defects['findings']))
        self.assertEqual('failed', defects['findings'][0]['documentId'])
        findings = {(f['documentId'], f['engine']): f for f in defects['findings']}
        self.assertEqual('diagnostic-expectations', findings['warning', 'wordnet']['finding'])
        self.assertEqual('editable-text-or-order', findings['editable', 'wordnet']['finding'])
        self.assertEqual([], findings['warning', 'aspose']['candidateSourceLocations'])
        self.assertTrue(findings['warning', 'wordnet']['candidateSourceLocations'])
        self.assertNotIn(('passing', 'wordnet'), findings)
        self.assertEqual(defects['findings'], sorted(defects['findings'], key=lambda f: (f['rank'], f['documentId'], f['engine'])))

    def test_retention_gate_rejects_empty_duplicate_and_misattributed_findings(self):
        result = self.measured('loss'); result['editableContent']['passed'] = False
        report = self.report([result]); defects = comparison.defect_report(report, self.documents([result]))
        comparison.validate_defect_report(report, defects)
        for change in ('empty', 'duplicate', 'engine'):
            with self.subTest(change=change):
                broken = copy.deepcopy(defects)
                if change == 'empty': broken['findings'] = []
                elif change == 'duplicate': broken['findings'] *= 2
                else: broken['findings'][0]['engine'] = 'aspose'
                with self.assertRaises(ValueError): comparison.validate_defect_report(report, broken)

    def test_secondary_failed_verdicts_are_not_lost_in_a_primary_finding(self):
        result = self.measured('multiple')
        result['knownLossChecks']['passed'] = result['editableContent']['passed'] = False
        result['referenceChecks'][0].update(textAndOrderMatches=False, glyphPlacementPasses=False, textAndGeometryPassed=False)
        report = self.report([result]); defects = comparison.defect_report(report, self.documents([result]))
        comparison.validate_defect_report(report, defects)
        self.assertEqual(['editable-text-or-order', 'diagnostic-expectations', 'reference-text-or-order', 'glyph-placement'], defects['findings'][0]['failedVerdicts'])
        aggregate = self.measured('aggregate'); aggregate['referenceChecks'][0]['textAndGeometryPassed'] = False
        self.assertEqual([(9, 'reference-text-or-geometry')], comparison.failure_verdicts(aggregate))

    def test_cohorts_form_a_complete_disjoint_partition(self):
        manifest = json.loads((comparison.ROOT / 'tests/wordnet.Conversion.Tests/Fixtures/Comparison/manifest.json').read_text())
        selected = {cohort: {d['id'] for d in manifest['documents'] if comparison.in_cohort(d, cohort)} for cohort in comparison.COHORTS}
        self.assertEqual(12, len(selected['baseline'])); self.assertEqual(12, len(selected['p0-2026-10']))
        self.assertFalse(selected['baseline'] & selected['p0-2026-10'])
        self.assertEqual(selected['all'], selected['baseline'] | selected['p0-2026-10'])

    def test_continuous_fixture_changes_from_one_to_two_columns(self):
        path = comparison.ROOT / 'tests/wordnet.Conversion.Tests/Fixtures/Comparison/continuous-two-columns.docx'
        w = '{http://schemas.openxmlformats.org/wordprocessingml/2006/main}'
        with ZipFile(path) as archive: root = ElementTree.fromstring(archive.read('word/document.xml'))
        first = root.find('.//' + w + 'pPr/' + w + 'sectPr')
        final = root.find(w + 'body/' + w + 'sectPr')
        self.assertIsNotNone(first); self.assertIsNotNone(final)
        self.assertIsNone(first.find(w + 'cols'))
        self.assertEqual('2', final.find(w + 'cols').attrib[w + 'num'])
        self.assertEqual('continuous', final.find(w + 'type').attrib[w + 'val'])

    def test_automation_manifest_matches_generator_templates_and_declared_pages(self):
        path = comparison.ROOT / 'examples/WordNet.Automation/Templates/manifest.json'
        manifest = json.loads(path.read_text())
        self.assertEqual(manifest['generatorSha256'], comparison.digest(comparison.ROOT / manifest['generator']))
        self.assertEqual({'invoice': 1, 'report': 2}, {d['id']: d['expectedPages'] for d in manifest['documents']})
        for document in manifest['documents']:
            self.assertEqual(document['sha256'], comparison.digest(path.parent / document['path']))

    def test_capture_rejects_hosts_without_qualified_fontconfig_isolation(self):
        for platform in ('darwin', 'win32'):
            with self.subTest(platform=platform), patch.object(capture.sys, 'platform', platform), \
                 patch.object(capture.shutil, 'which', return_value=None), \
                 patch.object(capture.sys, 'argv', ['capture', '--output', 'unused']), contextlib.redirect_stderr(io.StringIO()) as error:
                with self.assertRaises(SystemExit) as exit_code: capture.main()
                self.assertEqual(2, exit_code.exception.code)
                self.assertIn('requires Linux', error.getvalue())

    def test_capture_resolves_bare_office_executable_from_path_before_hashing(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory); binary = root / 'bin' / 'wordnet-test-office'
            binary.parent.mkdir(); binary.write_text('#!/bin/sh\nexit 0\n'); binary.chmod(0o755)
            source = comparison.ROOT / 'tests/wordnet.Conversion.Tests/Fixtures/Comparison/mixed-fonts-short.docx'
            fonts = json.loads((source.parent / 'manifest.json').read_text())['fonts']
            manifest = root / 'manifest.json'
            manifest.write_text(json.dumps(dict(fonts=fonts, documents=[dict(id=source.stem, path=str(source), sha256=comparison.digest(source), cohort='p0-2026-10')])))
            output = root / 'capture'
            def invoke(command, **kwargs):
                if '--version' in command:
                    from subprocess import CompletedProcess
                    return CompletedProcess(command, 0, stdout='LibreOffice test build\n')
                pdf(output / (source.stem + '.pdf'))
            with patch.dict(os.environ, PATH=str(binary.parent) + os.pathsep + os.environ['PATH']), \
                 patch.object(capture.sys, 'platform', 'linux'), \
                 patch.object(capture.sys, 'argv', ['capture', '--manifest', str(manifest), '--output', str(output), '--soffice', binary.name, '--renderer', 'test-renderer']), \
                 patch.object(capture.comparison, 'invoke', side_effect=invoke), \
                 patch.object(capture.comparison, 'renderer_version', return_value='test-renderer'), contextlib.redirect_stdout(io.StringIO()):
                capture.main()
            reference = json.loads((output / 'references.json').read_text())['references'][0]
            self.assertEqual(comparison.digest(binary), reference['executableSha256'])
            self.assertEqual(comparison.digest(output / reference['pdf']), reference['sha256'])

    def test_actual_pdf_negative_controls(self):
        dotnet = os.environ.get('WORDNET_DOTNET') or shutil.which('dotnet')
        worker = comparison.ROOT / 'tools/wordnet.Benchmarks/bin/Release/net10.0/wordnet.Benchmarks.dll'
        renderer = shutil.which('pdftoppm')
        self.assertTrue(dotnet and renderer and worker.is_file(), 'Build the benchmark project; install dotnet and Poppler.')
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            snapshots, rasters = {}, {}
            for name, args in dict(reference={}, shifted=dict(x=62), missing=dict(text='Invoice 1200 EUR'), pages=dict(pages=2)).items():
                path = root / (name + '.pdf'); pdf(path, **args)
                output = path.with_suffix('.json')
                comparison.invoke([dotnet, str(worker), '--comparison', 'snapshot', str(path), str(output)])
                snapshots[name] = json.loads(output.read_text())
                rasters[name] = comparison.render(path, root / name, renderer)
            same = comparison.snapshot_check(snapshots['reference'], snapshots['reference'])
            self.assertTrue(same['glyphPlacementPasses']); self.assertTrue(same['pageCountMatches'])
            shifted = comparison.snapshot_check(snapshots['shifted'], snapshots['reference'])
            self.assertTrue(shifted['textAndOrderMatches']); self.assertFalse(shifted['glyphPlacementPasses'])
            self.assertAlmostEqual(12, shifted['maximumGlyphDisplacementPoints'])
            self.assertFalse(comparison.pixel_check(rasters['shifted'][0], rasters['reference'][0])['passed'])
            self.assertFalse(comparison.content_check(snapshots['missing'], ['Invoice total 1200 EUR'])['passed'])
            self.assertFalse(comparison.snapshot_check(snapshots['missing'], snapshots['reference'])['textAndOrderMatches'])
            self.assertFalse(comparison.snapshot_check(snapshots['pages'], snapshots['reference'])['pageCountMatches'])
            self.assertTrue(comparison.pixel_check(rasters['reference'][0], rasters['reference'][0])['passed'])
            # Reusing a capture prefix must not include old pages or reference rasters.
            prefix = root / 'reused'
            comparison.render(root / 'pages.pdf', prefix, renderer)
            comparison.render(root / 'pages.pdf', root / 'reused-reference', renderer)
            self.assertEqual(1, len(comparison.render(root / 'reference.pdf', prefix, renderer)))

    def test_reading_order_is_not_a_bag_of_words(self):
        snapshot = dict(pages=[dict(text='Second term. First term.')])
        self.assertFalse(comparison.content_check(snapshot, ['First term.', 'Second term.'])['passed'])

    def test_loss_expectations_reject_silent_and_unexpected_losses(self):
        expected = ['Table.NestedFlattened', 'Table.Decoration']
        warnings = [dict(code=code, severity='Warning') for code in expected]
        self.assertTrue(comparison.loss_check(expected, warnings)['passed'])
        self.assertFalse(comparison.loss_check(expected, warnings[:1])['passed'])
        self.assertEqual(['Table.Decoration'], comparison.loss_check(expected, warnings[:1])['missing'])
        self.assertFalse(comparison.loss_check([], warnings)['passed'])
        self.assertTrue(comparison.loss_check([], [dict(code='Docx.AuxiliaryMetadata', severity='Info')])['passed'])

    def test_renderer_identity_matches_windows_and_unix_captures(self):
        windows = 'pdftoppm version 25.06.0\r\nCopyright vendor\r\n'
        unix = 'pdftoppm version 25.06.0\nCopyright vendor\n'
        self.assertEqual(comparison.renderer_identity(windows), comparison.renderer_identity(unix))
        self.assertNotEqual(comparison.renderer_identity(unix), comparison.renderer_identity(unix.replace('25.06.0', '25.07.0')))

    def test_manifest_hashes_categories_and_independence(self):
        manifest_path = comparison.ROOT / 'tests/wordnet.Conversion.Tests/Fixtures/Comparison/manifest.json'
        manifest_bytes = manifest_path.read_bytes()
        self.assertNotIn(b'\r', manifest_bytes)
        manifest = json.loads(manifest_bytes)
        self.assertEqual(manifest['generatorSha256'], comparison.digest(comparison.ROOT / manifest['generator']))
        self.assertTrue(all('\\' not in f['path'] for f in manifest['fonts']))
        self.assertEqual(24, len(manifest['documents']))
        self.assertEqual(24, len({d['sha256'] for d in manifest['documents']}))
        extension = [d for d in manifest['documents'] if d.get('cohort') == 'p0-2026-10']
        self.assertEqual(12, len(extension))
        for feature in ('mixed-fonts', 'long-tables', 'footnotes', 'continuous-sections', 'floating-logos', 'multi-page-reports'):
            self.assertEqual(2, sum(d['feature'] == feature for d in extension))
        self.assertEqual({'invoice', 'contract', 'report', 'letterhead'}, {d['category'] for d in manifest['documents']})
        for doc in manifest['documents']:
            self.assertEqual(doc['sha256'], comparison.digest(manifest_path.parent / doc['path']))
        for font in manifest['fonts']:
            self.assertEqual(font['sha256'], comparison.digest(comparison.ROOT / font['path']))


if __name__ == '__main__':
    unittest.main()
