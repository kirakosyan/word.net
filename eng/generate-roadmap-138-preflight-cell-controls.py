#!/usr/bin/env python3
"""Independently author simple, merged and invalid control contrasts."""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('table_fixture', ROOT / 'eng/generate-roadmap-138-preflight-cells.py')
table_fixture = importlib.util.module_from_spec(spec)
spec.loader.exec_module(table_fixture)


def inline(tag):
    return ('<w:p><w:sdt><w:sdtPr><w:tag w:val="' + tag + '"/></w:sdtPr>'
            '<w:sdtContent><w:r><w:t>cached</w:t></w:r></w:sdtContent></w:sdt></w:p>')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', required=True, type=Path)
    destination = parser.parse_args().output.resolve()
    fixtures = (ROOT / 'tests/wordnet.Conversion.Tests/Fixtures').resolve()
    if destination == fixtures or fixtures in destination.parents:
        parser.error('Choose a new scratch directory outside the fixture tree.')
    destination.mkdir(parents=True, exist_ok=False)
    table, reductions = table_fixture.table, table_fixture.reductions
    root_repeat = ('<w:sdt><w:sdtPr><w:tag w:val="repeat:Bad-Name"/></w:sdtPr>'
                   '<w:sdtContent><w:p><w:r><w:t>cached</w:t></w:r></w:p></w:sdtContent></w:sdt>')
    cases = {'simple': inline('merge:Customer.Name') + table(inline('merge:Customer.Code')),
             'merged': table(inline('merge:Hidden'), merged=True), 'invalid': inline('merge:Bad-Name') + root_repeat}
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
