"""Protect evidence identity/completeness and deterministic independent reductions."""
import copy
import contextlib
import gzip
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
from zipfile import ZIP_DEFLATED, ZIP_STORED, ZipFile, ZipInfo

ROOT = Path(__file__).resolve().parents[1]


def module(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


baseline = module('baseline', ROOT / 'eng/refresh-fidelity-baseline.py')
comparison = module('comparison', ROOT / 'eng/run-comparison.py')
generator = module('reductions', ROOT / 'eng/generate-fidelity-reductions.py')
capture_index = module('capture_index', ROOT / 'eng/index-fidelity-capture.py')


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
        self.documents = [dict(id='example', path='example.docx', sha256='c' * 64)]
        self.manifest = dict(documents=self.documents)

    def defects(self, report):
        return comparison.defect_report(report, self.manifest)

    def test_known_reference_failure_remains_failure(self):
        self.assertEqual(1, baseline.verify_measurements(self.report, self.defects(self.report), comparison, self.documents, self.binaries))
        self.assertEqual(['page-geometry'], self.defects(self.report)['findings'][0]['failedVerdicts'])

    def test_missing_duplicate_or_uncaptured_document_is_rejected(self):
        for change in ('missing', 'duplicate', 'reference'):
            report = copy.deepcopy(self.report)
            if change == 'missing': report['results'] = []
            elif change == 'duplicate': report['results'] *= 2
            else: report['results'][0]['referenceChecks'] = [dict(status='not-provided')]
            with self.subTest(change=change), self.assertRaises(ValueError):
                baseline.verify_measurements(report, self.defects(report), comparison, self.documents, self.binaries)

    def test_duplicate_ids_are_rejected_even_when_count_and_all_verdicts_pass(self):
        result = copy.deepcopy(self.result)
        result['referenceChecks'][0].update(pageGeometryMatches=True, textAndGeometryPassed=True)
        report = dict(self.report, results=[result, copy.deepcopy(result)])
        defects = self.defects(report)
        self.assertEqual([], defects['findings'])
        comparison.validate_defect_report(report, defects)
        with self.assertRaisesRegex(ValueError, 'Missing or duplicate'):
            baseline.verify_measurements(report, defects, comparison, self.documents + [dict(id='second', sha256='d' * 64)], self.binaries)

    def test_matching_count_cannot_hide_wrong_id_digest_or_duplicate_manifest(self):
        for documents in ([dict(id='other', sha256='c' * 64)], [dict(id='example', sha256='d' * 64)],
                          self.documents * 2, []):
            with self.subTest(documents=documents), self.assertRaisesRegex(ValueError, 'input identity'):
                baseline.verify_measurements(self.report, self.defects(self.report), comparison, documents, self.binaries)

    def test_source_package_binary_substitution_is_rejected(self):
        for name in self.binaries:
            expected = dict(self.binaries, **{name: '0' * 64})
            with self.subTest(name=name), self.assertRaisesRegex(ValueError, 'unexpected binary'):
                baseline.verify_measurements(self.report, self.defects(self.report), comparison, self.documents, expected)

    def test_unused_embedded_font_assembly_is_not_claimed_as_executed(self):
        self.result['measurement']['assemblies'] = [a for a in self.result['measurement']['assemblies'] if a['name'] != 'wordnet.Fonts']
        self.assertEqual(1, baseline.verify_measurements(self.report, self.defects(self.report), comparison, self.documents, self.binaries))
        self.result['measurement']['assemblies'] = []
        with self.assertRaisesRegex(ValueError, 'unexpected binary'):
            baseline.verify_measurements(self.report, self.defects(self.report), comparison, self.documents, self.binaries)

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
                if document['id'].startswith('table-'):
                    self.assertIn('cannot distinguish', document['hypothesis'])
                    document['hypothesis'] = measured['hypothesis']
                self.assert_package_contents_equal(first[document['path']], (committed / measured['path']).read_bytes())
            self.assertEqual(baseline.sha(ROOT / manifest['generator']), manifest['generatorSha256'])
            # The receipt records the original generator; current scratch generation
            # has its own source identity and deliberately does not rewrite history.
            manifest['generatorSha256'] = historical['generatorSha256']
            self.assertEqual(historical, manifest)
            self.assertEqual(before, {p.name: baseline.sha(p) for p in original.iterdir() if p.is_file()})

    def test_default_generation_preserves_measured_fixtures_and_refuses_explicit_overwrite(self):
        measured = ROOT / 'tests/wordnet.Conversion.Tests/Fixtures/Fidelity103'
        before = {p.name: p.read_bytes() for p in measured.iterdir() if p.is_file()}
        with tempfile.TemporaryDirectory() as directory, patch.object(generator, 'DEFAULT_OUTPUT', Path(directory)), \
             patch.object(sys, 'argv', ['generate']), contextlib.redirect_stdout(io.StringIO()):
            generator.main()
            self.assertTrue((Path(directory) / 'manifest.json').is_file())
        command = [sys.executable, generator.__file__, '--output', str(measured)]
        result = subprocess.run(command, text=True, capture_output=True)
        self.assertEqual(2, result.returncode)
        self.assertIn('must not be overwritten', result.stderr)
        self.assertEqual(before, {p.name: p.read_bytes() for p in measured.iterdir() if p.is_file()})
        self.assertEqual(ROOT / 'artifacts/fidelity-103/reductions', generator.DEFAULT_OUTPUT)

    def test_new_zip_bytes_are_independent_of_platform_defaults_and_zlib(self):
        parts = {'word/document.xml': '<document>Example</document>', 'word/media/logo.png': b'\x89PNG\r\n\x1a\n'}
        with tempfile.TemporaryDirectory() as directory:
            outputs = []
            for system in (0, 3):
                def info(name, date):
                    value = ZipInfo(name, date)
                    value.create_system = system
                    value.external_attr = system + 1
                    return value
                path = Path(directory) / str(system)
                with patch.object(generator, 'ZipInfo', side_effect=info), \
                     patch('zlib.compressobj', side_effect=AssertionError('Deflate must not be used')):
                    generator.write_zip(path, parts)
                outputs.append(path.read_bytes())
                with ZipFile(path) as archive:
                    self.assertTrue(all(p.compress_type == ZIP_STORED and p.create_system == 0 and
                                        p.external_attr == 0o600 << 16 for p in archive.infolist()))
            self.assertEqual(*outputs)

    def test_missing_poi_fails_before_network_or_builds(self):
        with tempfile.TemporaryDirectory() as directory:
            dest = Path(directory)
            args = ['refresh', '--repository', str(dest / 'clean'), '--commit', 'revision',
                    '--output', str(dest / 'evidence'), '--poi-classpath', str(dest / 'runner-jars')]
            with patch.object(sys, 'argv', args), patch.object(baseline.urllib.request, 'urlopen') as network, \
                 patch.object(baseline.subprocess, 'check_output') as command, \
                 contextlib.redirect_stderr(io.StringIO()) as error, self.assertRaises(SystemExit) as failure:
                baseline.main()
            self.assertEqual(2, failure.exception.code)
            self.assertIn(str(dest / 'runner-jars'), error.getvalue())
            self.assertFalse((dest / 'evidence').exists())
            network.assert_not_called()
            command.assert_not_called()

    def test_poi_validation_rejects_changed_pinned_jars(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            jar = root / 'poi.jar'; jar.write_bytes(b'pinned jar')
            pins = [dict(name=jar.name, sha256=baseline.sha(jar))]
            self.assertEqual({jar.name: pins[0]['sha256']}, baseline.verify_poi(root, pins))
            jar.write_bytes(b'changed jar')
            with self.assertRaisesRegex(ValueError, 'Missing or changed POI jar'):
                baseline.verify_poi(root, pins)

    def test_registration_selection_excludes_unlisted_prerelease_and_fetches_pages(self):
        def entry(version, listed=True):
            return dict(catalogEntry=dict(version=version, listed=listed))
        pages = {baseline.REGISTRATION.format('wordnet'): dict(items=[dict(items=[entry('1.9.0'), entry('9.0.0', False), entry('10.0.0-preview.1')]),
                                                       {'@id': 'next-page'}]),
                 'next-page': dict(items=[entry('1.10.0'), entry('2.0.0', False)])}
        self.assertEqual('1.10.0', baseline.latest_listed_stable(lambda package, url: pages[url], 'wordnet'))
        with self.assertRaisesRegex(ValueError, 'no listed stable'):
            baseline.latest_listed_stable(lambda package, url: dict(items=[dict(items=[entry('9.0.0', False), entry('10.0.0-rc.1')])]), 'wordnet')

    def test_companion_registration_must_be_listed_and_match_core(self):
        def entries(*versions):
            return {'items': [{'items': [{'catalogEntry': {'version': version, 'listed': listed}}
                                         for version, listed in versions]}]}
        pages = {baseline.REGISTRATION.format(name): entries(('1.6.2', True)) for name in baseline.PACKAGE_IDS}
        self.assertEqual('1.6.2', baseline.matching_release(lambda package, url: pages[url]))
        for name in ('wordnet.fonts', 'wordnet.ocr'):
            for listing in (entries(('1.6.1', True), ('1.6.2', False)), entries(('1.7.0', True)),
                            entries(('1.7.0-preview.1', True))):
                with self.subTest(name=name, listing=listing), self.assertRaisesRegex(ValueError, name):
                    baseline.matching_release(lambda package, url: listing if package == name else pages[url])

    def test_archived_index_and_external_page_keep_their_package_identity(self):
        with tempfile.TemporaryDirectory() as folder:
            directory = Path(folder)
            evidence = []
            def fetch(package, url):
                data = (json.dumps({'items': [{'@id': 'external-page'}]}).encode() if url != 'external-page'
                        else gzip.compress(b'{"items":[{"catalogEntry":{"version":"1.6.2","listed":true}}]}'))
                with patch.object(baseline.urllib.request, 'urlopen', return_value=io.BytesIO(data)):
                    return baseline.archive_registration(directory, evidence, package, url)
            self.assertEqual('1.6.2', baseline.matching_release(fetch))
            self.assertEqual(6, len(set(row['file'] for row in evidence)))
            for index, row in enumerate(evidence):
                package = baseline.PACKAGE_IDS[index // 2]
                suffix = '.json.gz' if index % 2 else '.json'
                self.assertEqual(package, row['package'])
                self.assertEqual(f'{package}-registration-{index}{suffix}', row['file'])
                self.assertEqual(baseline.sha(directory / row['file']), row['sha256'])

    def test_registration_json_supports_real_feed_gzip_and_plain_responses(self):
        document = dict(items=[dict(items=[dict(catalogEntry=dict(version='1.4.0', listed=True))])])
        plain = json.dumps(document).encode()
        for data in (plain, gzip.compress(plain)):
            self.assertEqual(document, baseline.decode_json(data))

    def test_published_162_index_reproduces_every_rank_and_classification(self):
        evidence = ROOT / 'docs/measurements/fidelity-1.6.2'
        # Replay must survive changes to live thresholds, ranking and fixture manifests.
        with ZipFile(evidence / 'capture.zip') as capture, patch.object(capture_index.baseline, 'ROOT', Path('absent')), \
             patch.object(comparison, 'TOLERANCES', {}), patch.object(comparison, 'failure_verdicts', return_value=[]):
            rows = capture_index.index(capture)
        output = io.StringIO()
        capture_index.write_index(rows, output)
        self.assertEqual((evidence / 'findings.tsv').read_bytes(), output.getvalue().encode('utf-8'))
        # Unequal page counts cannot establish the cause of a geometry difference.
        for mode in ('source', 'package'):
            self.assertIn((7, 'reductions', 'notes-continuation', mode, 'page-geometry', 'unknown'), rows)
            self.assertIn((7, 'combinations', 'explicit-direct', mode, 'page-geometry', 'reference-policy-difference'), rows)
            self.assertIn((3, 'combinations', 'theme-styled', mode, 'diagnostic-expectations', 'documented-unsupported'), rows)

    def test_replay_receipt_only_adds_original_snapshot_hashes(self):
        with ZipFile(ROOT / 'docs/measurements/fidelity-1.6.2/capture.zip') as capture:
            replay = json.loads(capture.read('receipt.json'))
            original = json.loads(capture.read('qualification/original-receipt.json'))
            for cohort in replay['cohorts'].values():
                for hashes in cohort['rawHashes'].values():
                    hashes.pop('referenceSnapshots')
            self.assertEqual(original, replay)
            runner = capture.read('qualification/executed-runner.py')
            self.assertEqual(original['runnerSha256'], hashlib.sha256(runner).hexdigest())
            self.assertEqual('cbeb9d15b80c6fcb368748a6152bd2b1d1f8dc4b123717d2aa7a963f80ab7c53',
                             hashlib.sha256(runner.replace(b'\r\n', b'\n')).hexdigest())

    def test_capture_index_rejects_altered_raw_report_or_swapped_reference_snapshot(self):
        with ZipFile(ROOT / 'docs/measurements/fidelity-1.6.2/capture.zip') as capture:
            read = capture.read
            def tamper(name):
                raw = read(name)
                if name == altered:
                    if name.endswith(('/results.json', '/defects.json')):
                        return raw + b' '
                    snapshot = json.loads(raw)
                    snapshot['pages'][0]['text'] = 'Different reference text'
                    return json.dumps(snapshot).encode()
                return raw
            for altered, error in (('existing-24-source/results.json', 'hash mismatch'),
                                   ('combinations-package/defects.json', 'hash mismatch'),
                                   ('combinations-package/theme-styled-wordnet-reference.json', 'hash mismatch')):
                with self.subTest(altered=altered), patch.object(capture, 'read', tamper), self.assertRaisesRegex(ValueError, error):
                    capture_index.index(capture)

    def test_geometry_tampering_in_both_modes_preserves_verdict_but_fails_hash(self):
        with ZipFile(ROOT / 'docs/measurements/fidelity-1.6.2/capture.zip') as capture:
            read = capture.read
            def changed(name):
                raw = read(name)
                if name.startswith('combinations-') and name.endswith('/explicit-direct-wordnet-reference.json'):
                    snapshot = json.loads(raw)
                    snapshot['pages'][0]['width'] = 595.0
                    return json.dumps(snapshot).encode()
                return raw
            with patch.object(capture, 'read', changed), self.assertRaisesRegex(ValueError, 'reference hash mismatch'):
                capture_index.index(capture)

    def test_only_named_controls_in_the_measured_cohort_attribute_a_single_warning(self):
        diagnostics = dict(missing=[], unexpected=['Docx.ThemeFont'], passed=False, extraMetadata='advisory')
        for cohort, document, changes, expected in (
                ('combinations', 'theme-direct', {}, 'documented-unsupported'),
                ('unrelated', 'theme-direct', {}, 'unknown'),
                ('combinations', 'renamed-control', {}, 'unknown'),
                ('combinations', 'theme-styled', {'missing': ['Docx.TableFormatting']}, 'unknown'),
                ('combinations', 'theme-styled', {'unexpected': ['Docx.ThemeFont', 'Other']}, 'unknown')):
            with self.subTest(cohort=cohort, document=document, changes=changes):
                actual, _ = capture_index.policy.classification(cohort, document, 'diagnostic-expectations', [], [],
                                                                diagnostics | changes)
                self.assertEqual(expected, actual)

    def test_summary_reproduces_every_committed_count_and_timing_statistic(self):
        receipt = json.loads((ROOT / 'docs/measurements/fidelity-103-baseline.json').read_text())
        before = copy.deepcopy(receipt)
        self.assertEqual(receipt['summary'], baseline.summarize(receipt))
        self.assertEqual(before, receipt)
        receipt['cohorts']['reductions']['measurements']['package']['results'][0]['measurement']['outputSha256'] = '0' * 64
        self.assertFalse(baseline.summarize(receipt)['reductions']['sourcePackageSnapshotsAndVerdictsIdentical'])

    def test_summary_does_not_mislabel_authored_failures_as_reference_failures(self):
        result = copy.deepcopy(self.result)
        for key in ('pageCountMatches', 'pageGeometryMatches', 'textAndOrderMatches', 'glyphPlacementPasses', 'textAndGeometryPassed', 'visualPassed'):
            result['referenceChecks'][0][key] = True
        result['measurement']['outputSha256'] = 'a' * 64
        package = copy.deepcopy(result)
        result['authoredChecks']['passed'] = False
        receipt = dict(cohorts=dict(example=dict(measurements=dict(source=dict(results=[result]), package=dict(results=[package])))))
        summary = baseline.summarize(receipt)['example']
        self.assertEqual(0, summary['source']['referenceFailures'])
        self.assertEqual(0, summary['source']['authoredContentPasses'])
        self.assertFalse(summary['sourcePackageSnapshotsAndVerdictsIdentical'])

    def audit_fixture(self, root):
        raw = copy.deepcopy(self.report)
        defects = self.defects(raw)
        reference = root / 'example-office' / 'reference.pdf'
        reference.parent.mkdir(); reference.write_bytes(b'independent reference bytes')
        cohort = dict(manifest=self.manifest, references=dict(references=[dict(documentId='example',
            pdf=reference.name, sha256=baseline.sha(reference))]), measurements={}, defects={}, rawHashes={})
        for mode in ('source', 'package'):
            directory = root / ('example-' + mode); directory.mkdir()
            baseline.save(directory / 'results.json', raw); baseline.save(directory / 'defects.json', defects)
            cohort['measurements'][mode] = baseline.compact(raw)
            cohort['defects'][mode] = copy.deepcopy(defects)
            (directory / 'example-wordnet-reference.json').write_bytes(b'{}')
            cohort['rawHashes'][mode] = {kind: baseline.sha(directory / (kind + '.json')) for kind in ('results', 'defects')}
            cohort['rawHashes'][mode]['referenceSnapshots'] = baseline.reference_hashes(directory, self.documents)
        return dict(cohorts=dict(example=cohort), workers={mode: dict(binaries=self.binaries) for mode in ('source', 'package')},
            publicPackages={'wordnet': dict(entries={'lib/net10.0/wordnet.dll': self.binaries['wordnet.dll']}),
                'wordnet.fonts': dict(entries={'lib/netstandard2.0/wordnet.Fonts.dll': self.binaries['wordnet.Fonts.dll']})})

    def test_evidence_audit_checks_raw_files_and_does_not_claim_unrun_schema_validation(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory); receipt = self.audit_fixture(root)
            audit = baseline.audit_evidence(receipt, root, comparison)
            self.assertTrue(audit['allMeasuredBinariesMatchWorkers'])
            self.assertTrue(audit['everyIndependentReferenceHashVerified'])
            self.assertTrue(audit['compactReportsMatchRawReports'])
            self.assertEqual('not-run', audit['independentOpenXmlSchemas']['status'])

    def test_evidence_audit_rejects_changed_reference_raw_compact_and_package_assets(self):
        for change in ('reference', 'snapshot', 'raw', 'compact', 'package'):
            with self.subTest(change=change), tempfile.TemporaryDirectory() as directory:
                root = Path(directory); receipt = self.audit_fixture(root)
                if change == 'reference': (root / 'example-office/reference.pdf').write_bytes(b'changed reference')
                elif change == 'snapshot': (root / 'example-package/example-wordnet-reference.json').write_bytes(b'{ }')
                elif change == 'raw': (root / 'example-source/results.json').write_text('{}')
                elif change == 'compact': receipt['cohorts']['example']['measurements']['source']['results'][0]['authoredChecks']['passed'] = False
                else: receipt['publicPackages']['wordnet']['entries']['lib/net10.0/wordnet.dll'] = '0' * 64
                with self.assertRaises(ValueError): baseline.audit_evidence(receipt, root, comparison)

    def test_public_consumer_restores_public_graph_despite_conflicting_repository_pins(self):
        dotnet = shutil.which('dotnet')
        self.assertTrue(dotnet, 'Install the SDK from global.json before running these guards.')
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory); feed = root / 'public feed'; feed.mkdir()
            for name, version, dependencies in (('wordnet', '1.4.0', [('Graph.Dependency', '1.0.0')]),
                    ('Graph.Dependency', '1.0.0', []), ('Graph.Dependency', '2.0.0', []), ('BenchmarkDotNet', '0.15.8', [])):
                deps = ''.join(f'<dependency id="{n}" version="{v}"/>' for n, v in dependencies)
                with ZipFile(feed / f'{name}.{version}.nupkg', 'w') as archive:
                    archive.writestr(name + '.nuspec', f'<package><metadata><id>{name}</id><version>{version}</version>'
                        '<authors>wordnet test</authors><description>Synthetic dependency-graph regression fixture</description>'
                        f'<dependencies>{deps}</dependencies></metadata></package>')
            shutil.copyfile(ROOT / 'global.json', root / 'global.json')
            # Only synthetic packages are used; there is no vulnerability feed in this offline test.
            (root / 'Directory.Build.props').write_text('<Project><PropertyGroup><RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>'
                '<NuGetAudit>false</NuGetAudit></PropertyGroup></Project>')
            (root / 'Directory.Packages.props').write_text('<Project><PropertyGroup><ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>'
                '<CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled></PropertyGroup><ItemGroup>'
                '<PackageVersion Include="Graph.Dependency" Version="2.0.0"/>'
                '<PackageVersion Include="BenchmarkDotNet" Version="0.15.8"/></ItemGroup></Project>')
            project, config = baseline.prepare_package_project(root, root / 'consumer', feed, '1.4.0')
            # Keep the regression restore fully offline, including development packages.
            from xml.sax.saxutils import quoteattr
            config.write_text(f'<configuration><packageSources><clear/><add key="test-feed" value={quoteattr(str(feed))}/>'
                              '</packageSources></configuration>')
            command = [dotnet, 'restore', str(project), '--configfile', str(config), '--packages', str(root / 'cache')]
            for extra in ([], ['--locked-mode']):
                result = subprocess.run(command + extra, text=True, capture_output=True)
                self.assertEqual(0, result.returncode, result.stdout + result.stderr)
            packages = json.loads((project.parent / 'packages.lock.json').read_text())['dependencies']['net10.0']
            self.assertEqual('1.0.0', packages['Graph.Dependency']['resolved'])
            self.assertEqual('Transitive', packages['Graph.Dependency']['type'])
            self.assertFalse(any(p['type'] == 'CentralTransitive' for p in packages.values()))


if __name__ == '__main__':
    unittest.main()
