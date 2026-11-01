#!/usr/bin/env python3
"""Independently author simple and complex field-location contrasts."""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('table_fixture', ROOT / 'eng/generate-roadmap-138-preflight-cells.py')
table_fixture = importlib.util.module_from_spec(spec)
spec.loader.exec_module(table_fixture)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', required=True, type=Path)
    destination = parser.parse_args().output.resolve()
    fixtures = (ROOT / 'tests/wordnet.Conversion.Tests/Fixtures').resolve()
    if destination == fixtures or fixtures in destination.parents:
        parser.error('Choose a new scratch directory outside the fixture tree.')
    destination.mkdir(parents=True, exist_ok=False)
    field, table, reductions = table_fixture.field, table_fixture.table, table_fixture.reductions
    complex_field = ('<w:p><w:r><w:fldChar w:fldCharType="begin"/></w:r>'
                     '<w:r><w:instrText xml:space="preserve"> MERGEFIELD Secret </w:instrText></w:r>'
                     '<w:r><w:fldChar w:fldCharType="separate"/></w:r><w:r><w:t>cached</w:t></w:r>'
                     '<w:r><w:fldChar w:fldCharType="end"/></w:r></w:p>')
    cases = {'simple': field('Customer.Name') + table(field('Customer.Code')), 'complex': complex_field}
    hashes = {}
    for name, body in cases.items():
        path = destination / (name + '.docx')
        reductions.write_zip(path, reductions.package(body))
        hashes[path.name] = hashlib.sha256(path.read_bytes()).hexdigest()
    manifest = {'provenance': 'Synthetic OOXML independently authored with the MIT fixture helpers; no wordnet serialization.',
                'generatorSha256': hashlib.sha256(Path(__file__).read_bytes()).hexdigest(), 'documents': hashes}
    (destination / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(manifest))


if __name__ == '__main__':
    main()
