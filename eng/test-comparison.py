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

    def test_manifest_hashes_categories_and_independence(self):
        manifest_path = comparison.ROOT / 'tests/wordnet.Conversion.Tests/Fixtures/Comparison/manifest.json'
        manifest = json.loads(manifest_path.read_text())
        self.assertEqual(12, len(manifest['documents']))
        self.assertEqual(12, len({d['sha256'] for d in manifest['documents']}))
        self.assertEqual({'invoice', 'contract', 'report', 'letterhead'}, {d['category'] for d in manifest['documents']})
        for doc in manifest['documents']:
            self.assertEqual(doc['sha256'], comparison.digest(manifest_path.parent / doc['path']))
        for font in manifest['fonts']:
            self.assertEqual(font['sha256'], comparison.digest(comparison.ROOT / font['path']))


if __name__ == '__main__':
    unittest.main()
