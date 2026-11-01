#!/usr/bin/env python3
"""Independently author repeat and root-owner scope contrasts."""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('fixtures', ROOT / 'eng/generate-roadmap-138-preflight-cells.py')
fixtures = importlib.util.module_from_spec(spec)
spec.loader.exec_module(fixtures)


def control(path):
    return ('<w:p><w:sdt><w:sdtPr><w:tag w:val="merge:' + path + '"/></w:sdtPr>'
            '<w:sdtContent><w:r><w:t>cached</w:t></w:r></w:sdtContent></w:sdt></w:p>')


def repeat(content):
    return ('<w:sdt><w:sdtPr><w:tag w:val="repeat:Items"/></w:sdtPr>'
            '<w:sdtContent>' + content + '</w:sdtContent></w:sdt>')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', required=True, type=Path)
    destination = parser.parse_args().output.resolve()
    fixtures_dir = (ROOT / 'tests/wordnet.Conversion.Tests/Fixtures').resolve()
    if destination == fixtures_dir or fixtures_dir in destination.parents:
        parser.error('Choose a new scratch directory outside the fixture tree.')
    destination.mkdir(parents=True, exist_ok=False)
    field, table = fixtures.field, fixtures.table
    plain = '<w:p><w:r><w:t>cached</w:t></w:r></w:p>'
    cases = {
        'root': repeat(field('Inside')) + field('Outside') + table(control('Cell')) + control('Body'),
        'wrapped': repeat(plain) + '<w:customXml w:element="value">' + field('Wrapped') + '</w:customXml>',
        'repeat_table': repeat(table(field('Hidden'))) + field('Outside'),
    }
    hashes = {}
    for name, body in cases.items():
        path = destination / (name + '.docx')
        fixtures.reductions.write_zip(path, fixtures.reductions.package(body))
        hashes[path.name] = hashlib.sha256(path.read_bytes()).hexdigest()
    manifest = {'provenance': 'Synthetic OOXML independently authored with MIT fixture helpers; no wordnet serialization.',
                'generatorSha256': hashlib.sha256(Path(__file__).read_bytes()).hexdigest(), 'documents': hashes}
    (destination / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(manifest))


if __name__ == '__main__':
    main()
