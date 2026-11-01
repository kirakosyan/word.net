#!/usr/bin/env python3
"""Independently author synthetic template-lint inputs and data failures."""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('fixtures', ROOT / 'eng/generate-roadmap-138-preflight-cells.py')
fixtures = importlib.util.module_from_spec(spec)
spec.loader.exec_module(fixtures)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', required=True, type=Path)
    output = parser.parse_args().output.resolve()
    fixture_tree = (ROOT / 'tests/wordnet.Conversion.Tests/Fixtures').resolve()
    if output == fixture_tree or fixture_tree in output.parents:
        parser.error('Choose a new scratch directory outside the fixture tree.')
    output.mkdir(parents=True, exist_ok=False)
    cases = {
        'template.docx': fixtures.field('Customer.Name') + fixtures.field('Total'),
        'undeclared.docx': fixtures.field('Customer.Name') + fixtures.field('Hidden'),
    }
    for name, body in cases.items():
        fixtures.reductions.write_zip(output / name, fixtures.reductions.package(body))
    (output / 'bad-docx.docx').write_bytes(b'not an OPC package; lintprivate')
    malformed = fixtures.reductions.package('')
    malformed['word/document.xml'] = '<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body>lintprivate'
    fixtures.reductions.write_zip(output / 'bad-xml.docx', malformed)
    datasets = {
        'valid.json': b'{"Customer":{"Name":"lintprivate"},"Total":12.5}',
        'missing.json': b'{"Customer":{},"Total":12.5}',
        'wrong-type.json': b'{"Customer":{"Name":"lintprivate"},"Total":"not-number"}',
        'duplicate.json': b'{"Customer":{"Name":"lintprivate"},"Total":1,"Total":2}',
        'bad-utf8.json': b'{"Customer":"\xff"}',
        'oversized.json': b'{"Customer":{"Name":"' + b'x' * 4194304 + b'"},"Total":1}',
    }
    for name, value in datasets.items():
        (output / name).write_bytes(value)
    hashes = {path.name: hashlib.sha256(path.read_bytes()).hexdigest() for path in sorted(output.iterdir())}
    manifest = {'provenance': 'Synthetic OOXML and JSON independently authored with MIT fixture helpers; no caller data.',
                'generatorSha256': hashlib.sha256(Path(__file__).read_bytes()).hexdigest(), 'files': hashes}
    (output / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(manifest))


if __name__ == '__main__':
    main()
