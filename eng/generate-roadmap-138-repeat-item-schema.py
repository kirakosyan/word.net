#!/usr/bin/env python3
"""Independently author declared repeat-item and fallback scope contrasts."""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('fixtures', ROOT / 'eng/generate-roadmap-138-preflight-cells.py')
fixtures = importlib.util.module_from_spec(spec)
spec.loader.exec_module(fixtures)


def repeat(name, content):
    return ('<w:sdt><w:sdtPr><w:tag w:val="repeat:' + name + '"/></w:sdtPr>'
            '<w:sdtContent>' + content + '</w:sdtContent></w:sdt>')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', required=True, type=Path)
    output = parser.parse_args().output.resolve()
    fixture_tree = (ROOT / 'tests/wordnet.Conversion.Tests/Fixtures').resolve()
    if output == fixture_tree or fixture_tree in output.parents:
        parser.error('Choose a new scratch directory outside the fixture tree.')
    output.mkdir(parents=True, exist_ok=False)
    field = fixtures.field
    end = '<w:p><w:r><w:t>end</w:t></w:r></w:p>'
    cases = {
        'required': field('Name') + repeat('Items', field('Name')) + end,
        'optional': field('Name') + repeat('Items', field('Name')) + end,
        'wrong-scope': field('Name') + repeat('Items', field('Name')) + end,
        'two-scopes': repeat('Items', field('Name')) + repeat('Orders', field('Name')) + end,
        'nested': repeat('Items', field('Customer.Name')) + end,
        'number': repeat('Items', field('Amount')) + end,
        'mismatch': repeat('Items', field('Amount')) + end,
        'mismatch-empty': repeat('Items', field('Amount')) + end,
    }
    hashes = {}
    for name, body in cases.items():
        path = output / (name + '.docx')
        fixtures.reductions.write_zip(path, fixtures.reductions.package(body))
        hashes[path.name] = hashlib.sha256(path.read_bytes()).hexdigest()
    manifest = {'provenance': 'Synthetic OOXML independently authored with MIT fixture helpers; no wordnet serialization.',
                'generatorSha256': hashlib.sha256(Path(__file__).read_bytes()).hexdigest(), 'documents': hashes}
    (output / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(manifest))


if __name__ == '__main__':
    main()
