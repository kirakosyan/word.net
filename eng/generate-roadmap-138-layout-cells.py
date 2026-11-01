#!/usr/bin/env python3
"""Independently author simple table-cell layout snapshot specimens."""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('reductions', ROOT / 'eng/generate-fidelity-reductions.py')
reductions = importlib.util.module_from_spec(spec)
spec.loader.exec_module(reductions)


def table(rows):
    properties = '<w:tblPr><w:tblW w:w="6000" w:type="dxa"/><w:tblLayout w:type="fixed"/></w:tblPr>'
    grid = '<w:tblGrid><w:gridCol w:w="3000"/><w:gridCol w:w="3000"/></w:tblGrid>'
    result = []
    for index, row in enumerate(rows):
        header = '<w:trPr><w:tblHeader/></w:trPr>' if index == 0 else ''
        cells = ''.join('<w:tc><w:tcPr><w:tcW w:w="3000" w:type="dxa"/></w:tcPr>'
                        + reductions.p(text, reductions.EXPLICIT, 22) + '</w:tc>' for text in row)
        result.append('<w:tr>' + header + cells + '</w:tr>')
    return '<w:tbl>' + properties + grid + ''.join(result) + '</w:tbl>'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', required=True, type=Path)
    args = parser.parse_args()
    destination = args.output.resolve()
    fixtures = (ROOT / 'tests/wordnet.Conversion.Tests/Fixtures').resolve()
    if destination == fixtures or fixtures in destination.parents:
        parser.error('Choose a new scratch directory outside the fixture tree.')
    destination.mkdir(parents=True, exist_ok=False)
    cases = {'cells': [('MMMM', 'IIII'), ('OOOO', 'HHHH')],
             'headers': [('MMMM', 'IIII')] + [('OOOO', 'HHHH')] * 70}
    hashes = {}
    for name, rows in cases.items():
        path = destination / (name + '.docx')
        reductions.write_zip(path, reductions.package(table(rows)))
        hashes[path.name] = hashlib.sha256(path.read_bytes()).hexdigest()
    manifest = {'provenance': 'Synthetic OOXML authored without wordnet serialization; existing MIT fixture helper and embedded Noto Sans.',
                'generatorSha256': hashlib.sha256(Path(__file__).read_bytes()).hexdigest(), 'documents': hashes}
    (destination / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(manifest))


if __name__ == '__main__':
    main()
