#!/usr/bin/env python3
"""Negative controls for accepting minimum-runtime qualification evidence."""

import copy
from contextlib import redirect_stderr, redirect_stdout
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest import mock
import zipfile

spec = importlib.util.spec_from_file_location('linux_vm', Path(__file__).with_name('run-linux-vm.py'))
vm = importlib.util.module_from_spec(spec)
spec.loader.exec_module(vm)
storage_spec = importlib.util.spec_from_file_location(
    'vm_storage', Path(__file__).with_name('select-linux-vm-storage.py'))
storage = importlib.util.module_from_spec(storage_spec)
storage_spec.loader.exec_module(storage)


class StorageSelectionTests(unittest.TestCase):
    def setUp(self):
        self.workspace = Path('/checkout/artifacts')
        self.temporary = Path('/mnt')
        self.required = 60 * 1024**3

    def usage(self, workspace_free, temporary_free):
        def disk_usage(path):
            return mock.Mock(free=workspace_free if path == self.workspace else temporary_free)
        return mock.patch.object(storage.shutil, 'disk_usage', side_effect=disk_usage)

    def test_checkout_is_preferred_at_the_full_required_boundary(self):
        with self.usage(self.required, self.required + 1), \
                mock.patch.object(Path, 'is_mount', side_effect=AssertionError('unneeded mount probe')):
            self.assertEqual('workspace', storage.choose_storage(self.workspace, self.temporary))

    def test_separate_disk_with_full_required_space_is_used_when_checkout_is_short(self):
        with self.usage(self.required - 1, self.required), \
                mock.patch.object(Path, 'is_symlink', return_value=False), \
                mock.patch.object(Path, 'is_mount', return_value=True), \
                mock.patch.object(storage.subprocess, 'run', return_value=mock.Mock(stdout='ext4\n')):
            self.assertEqual('temporary', storage.choose_storage(self.workspace, self.temporary))

    def test_insufficient_unmounted_symlink_and_memory_backed_temp_fall_back(self):
        cases = [(False, True, 'ext4', self.required - 1),
                 (False, False, 'ext4', self.required + 1),
                 (True, True, 'ext4', self.required + 1),
                 (False, True, 'tmpfs', self.required + 1),
                 (False, True, 'ramfs', self.required + 1),
                 (False, True, '', self.required + 1)]
        for symlink, mounted, filesystem, free in cases:
            with self.subTest(symlink=symlink, mounted=mounted, filesystem=filesystem, free=free), \
                    self.usage(self.required - 1, free), \
                    mock.patch.object(Path, 'is_symlink', return_value=symlink), \
                    mock.patch.object(Path, 'is_mount', return_value=mounted), \
                    mock.patch.object(storage.subprocess, 'run', return_value=mock.Mock(stdout=filesystem)):
                self.assertEqual('workspace', storage.choose_storage(self.workspace, self.temporary))

    def test_uninspectable_mount_still_runs_the_launchers_workspace_preflight(self):
        output, errors = io.StringIO(), io.StringIO()
        with mock.patch.object(storage, 'choose_storage', side_effect=OSError('mount unavailable')), \
                mock.patch('sys.argv', ['select-linux-vm-storage.py', '--workspace', str(self.workspace)]), \
                redirect_stdout(output), redirect_stderr(errors):
            storage.main()
        self.assertEqual('workspace\n', output.getvalue())
        self.assertIn('mount unavailable', errors.getvalue())


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

    def test_additional_passing_cases_are_counted_without_weakening_the_minimum(self):
        self.counters = {'total': 831, 'passed': 831, 'notExecuted': 0, 'failed': 0}
        self.write()
        self.assertEqual(831, vm.validate_results(self.results, self.manifest)['cases'])

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
                         {'total': 831, 'passed': 830, 'notExecuted': 1},
                         {'total': 831, 'passed': 830, 'failed': 1},
                         {'total': 831, 'passed': 831, 'executed': 830},
                         {'total': 831, 'passed': 831, 'inconclusive': 1},
                         {'total': 831, 'passed': 831, 'passedButRunAborted': 1},
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
