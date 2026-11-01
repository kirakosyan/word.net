#!/usr/bin/env python3
"""Independently author simple and rejected table owners for block-control creation."""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('reductions', ROOT / 'eng/generate-fidelity-reductions.py')
reductions = importlib.util.module_from_spec(spec)
spec.loader.exec_module(reductions)


def cell(content='<w:p/>', properties=''):
    return '<w:tc><w:tcPr><w:tcW w:w="2000" w:type="dxa"/>' + properties + '</w:tcPr>' + content + '</w:tc>'


def table(rows):
    return ('<w:tbl><w:tblPr><w:tblW w:w="6000" w:type="dxa"/><w:tblLayout w:type="fixed"/></w:tblPr>'
            '<w:tblGrid>' + '<w:gridCol w:w="2000"/>' * 3 + '</w:tblGrid>'
            + ''.join('<w:tr>' + row + '</w:tr>' for row in rows) + '</w:tbl>')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', required=True, type=Path)
    target = parser.parse_args().output.resolve()
    fixture_tree = (ROOT / 'tests/wordnet.Conversion.Tests/Fixtures').resolve()
    if target == fixture_tree or fixture_tree in target.parents:
        parser.error('Choose a new scratch directory outside the fixture tree.')
    target.mkdir(parents=True, exist_ok=False)
    plain = reductions.p('Before') + table([cell() * 3, cell() * 3]) + reductions.p('After')
    styled = table([cell('<w:p><w:r><w:rPr><w:b/></w:rPr><w:t/></w:r></w:p>') + cell() * 2])
    merged = table([cell(properties='<w:gridSpan w:val="2"/>') + cell()])
    nested = table([cell('<w:p/>' + table([cell() * 3]) + '<w:p/>') + cell() * 2])
    hashes = {}
    for name, body in {'plain': plain, 'styled': styled, 'merged': merged, 'nested': nested}.items():
        path = target / (name + '.docx')
        reductions.write_zip(path, reductions.package(body))
        hashes[path.name] = hashlib.sha256(path.read_bytes()).hexdigest()
    manifest = {'provenance': 'Synthetic OOXML independently authored with MIT fixture helpers; no wordnet serialization.',
                'generatorSha256': hashlib.sha256(Path(__file__).read_bytes()).hexdigest(), 'documents': hashes}
    (target / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(manifest))


if __name__ == '__main__':
    main()
