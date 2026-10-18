#!/usr/bin/env python3
"""Negative controls for accepting minimum-runtime qualification evidence."""

import copy
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
import zipfile

spec = importlib.util.spec_from_file_location('linux_vm', Path(__file__).with_name('run-linux-vm.py'))
vm = importlib.util.module_from_spec(spec)
spec.loader.exec_module(vm)


class EvidenceTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.results = Path(self.temp.name)
        self.manifest = {'sourceCommit': 'a' * 40, 'testAsset': 'source', 'testsSha256': 'b' * 64,
                         'requiredCases': 829, 'jitMode': 'legacy',
                         'binaries': {'wordnet.dll': 'c' * 64, 'wordnet.Conversion.Tests.exe': 'd' * 64}}
        self.status = {'passed': True, 'frameworkRelease': 461814, 'exactFramework472': True,
                       'sourceCommit': 'a' * 40, 'testAsset': 'source', 'testsSha256': 'b' * 64}
        self.environment = {'exactFramework472': True, 'frameworkRelease': 461814,
                            'testProcessArchitecture': 'x64', 'requestedJitMode': 'legacy',
                            'runtimeOverrides': {}, 'childRuntimeOverrides': {'COMPlus_UseLegacyJit': '1'},
                            'loadedJitModules': [{'name': 'compatjit.dll'}],
                            'testConfigurationSha256': 'e' * 64,
                            'librarySha256': 'c' * 64, 'testSha256': 'd' * 64}
        self.counters = {'total': 829, 'passed': 829, 'notExecuted': 0, 'failed': 0}

    def write(self):
        (self.results / 'status.json').write_text(json.dumps(self.status))
        (self.results / 'environment.json').write_text(json.dumps(self.environment))
        attributes = ' '.join(f'{key}="{value}"' for key, value in self.counters.items())
        (self.results / 'net472.trx').write_text(
            f'<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">'
            f'<ResultSummary><Counters {attributes}/></ResultSummary></TestRun>')

    def test_complete_source_and_package_reports_are_accepted(self):
        for asset in ['source', 'package']:
            with self.subTest(asset=asset):
                self.manifest['testAsset'] = self.status['testAsset'] = asset
                self.write()
                self.assertEqual(829, vm.validate_results(self.results, self.manifest)['cases'])

    def test_guest_failure_newer_runtime_and_foreign_bundle_are_rejected(self):
        for key, value in [('passed', False), ('exactFramework472', False), ('frameworkRelease', 533320),
                           ('sourceCommit', 'f' * 40), ('testAsset', 'package'), ('testsSha256', 'f' * 64)]:
            with self.subTest(key=key):
                original = copy.deepcopy(self.status)
                self.status[key] = value
                self.write()
                with self.assertRaises(ValueError):
                    vm.validate_results(self.results, self.manifest)
                self.status = original

    def test_missing_runtime_jit_configuration_and_executed_binary_evidence_is_rejected(self):
        changes = [('frameworkRelease', 533320), ('exactFramework472', False),
                   ('testProcessArchitecture', 'x86'), ('requestedJitMode', 'default'),
                   ('loadedJitModules', [{'name': 'clrjit.dll'}]), ('loadedJitModules', []),
                   ('runtimeOverrides', {'COMPlus_ZapDisable': '1'}), ('childRuntimeOverrides', {}),
                   ('testConfigurationSha256', None), ('librarySha256', '0' * 64), ('testSha256', '0' * 64)]
        for key, value in changes:
            with self.subTest(key=key, value=value):
                original = copy.deepcopy(self.environment)
                self.environment[key] = value
                self.write()
                with self.assertRaises(ValueError):
                    vm.validate_results(self.results, self.manifest)
                self.environment = original

    def test_skips_failures_timeouts_and_partial_suites_are_rejected(self):
        for counters in [{'total': 829, 'passed': 828, 'notExecuted': 1},
                         {'total': 829, 'passed': 828, 'failed': 1},
                         {'total': 828, 'passed': 828},
                         {'total': 829, 'passed': 829, 'timeout': 1}]:
            with self.subTest(counters=counters):
                self.counters = counters
                self.write()
                with self.assertRaises(ValueError):
                    vm.validate_results(self.results, self.manifest)

    def test_incomplete_export_is_not_a_pass(self):
        self.write()
        (self.results / 'net472.trx').unlink()
        with self.assertRaises(FileNotFoundError):
            vm.validate_results(self.results, self.manifest)

    def test_setup_rejects_changed_zip_wrong_revision_and_wrong_asset(self):
        binaries = {'wordnet.dll': b'library', 'wordnet.Conversion.Tests.exe': b'tests'}
        with zipfile.ZipFile(self.results / 'tests.zip', 'w') as archive:
            for name, data in binaries.items():
                archive.writestr(name, data)
            archive.writestr('required-tests.txt', '829')
        manifest = copy.deepcopy(self.manifest)
        manifest['binaries'] = {name: hashlib.sha256(data).hexdigest() for name, data in binaries.items()}
        manifest['testsSha256'] = vm.sha256(self.results / 'tests.zip')
        (self.results / 'bundle.json').write_text(json.dumps(manifest))
        vm.validate_setup(self.results, 'a' * 40, 'source')
        for commit, asset in [('f' * 40, 'source'), ('a' * 40, 'package')]:
            with self.subTest(commit=commit, asset=asset):
                with self.assertRaises(ValueError):
                    vm.validate_setup(self.results, commit, asset)
        with (self.results / 'tests.zip').open('ab') as stream:
            stream.write(b'changed')
        with self.assertRaises(ValueError):
            vm.validate_setup(self.results, 'a' * 40, 'source')


if __name__ == '__main__':
    unittest.main()
