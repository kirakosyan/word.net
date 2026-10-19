"""Negative controls exercise actual independent PDFs, extraction and raster metrics."""
import importlib.util
import json
import os
from pathlib import Path
import shutil
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('comparison', Path(__file__).with_name('run-comparison.py'))
comparison = importlib.util.module_from_spec(spec)
spec.loader.exec_module(comparison)


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
